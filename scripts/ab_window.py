#!/usr/bin/env python3
"""Resource usage over ONE measured k6 window → <run-dir>/resources.json (A/B harness v1, H1).

The window comes from the run's journeys-*.json (main.js handleSummary: end of setup → + nominal scenario length),
so the k6 setup phase (200 logins: Argon2id on A vs PBKDF2 on B) is excluded — Day-0 averaged it in.

Per pod of the target namespace (minikube cAdvisor exports pod-slice series only → container=""):
  cpu_core_s   increase(container_cpu_usage_seconds_total[W])          CPU core-seconds spent in the window
  mem_avg_mib  avg_over_time(container_memory_working_set_bytes[W:15s])
  mem_max_mib  max_over_time(...)
(Network bytes are NOT taken from cAdvisor: this minikube/calico setup exports no pod network series — the transfer
per journey comes from k6's own data_received{scenario:http} in the journeys block instead.)
Pods are classified (app / db / bus / storage / frontend / other); per class and for the whole namespace the CPU is
also divided by the HTTP journeys served in the window (cpu_ms_per_journey — the H1 unit). The shared ingress-nginx
controller is reported separately (both targets' compression happens there) and the node's busy cores give context.

With --idle-start-ms/--idle-end-ms the same per-class CPU is also read over a short NO-TRAFFIC window after the run
(k6 finished, pods still up): the "idle" section records the fixed cost of each class in cores (A: RabbitMQ + outbox
relays, B: River polling, …) and marginal_cpu_ms_per_journey = (cpu_core_s − idle_cores × window_s) / journeys, so
the H1 tables can separate the per-journey cost from the standing cost of each architecture.

Usage: ab_window.py --prom URL --ns NAMESPACE --app-prefix backend- --run-dir DIR --target a|b
                    [--idle-start-ms MS --idle-end-ms MS]
"""
import argparse
import glob
import json
import sys
import time
import urllib.parse
import urllib.request

CLASSES = (("db", ("postgres-",)), ("bus", ("rabbitmq-", "redis-")), ("storage", ("minio-",)), ("frontend", ("frontend-",)))


def query(prom, expr, at):
    url = f"{prom}/api/v1/query?" + urllib.parse.urlencode({"query": expr, "time": f"{at:.3f}"})
    with urllib.request.urlopen(url, timeout=30) as response:
        body = json.load(response)
    if body.get("status") != "success":
        raise RuntimeError(f"{expr}: {body}")
    return body["data"]["result"]


def by_pod(result, scale=1.0):
    return {r["metric"].get("pod", "?"): float(r["value"][1]) * scale for r in result}


def scalar(result, scale=1.0):
    return sum(float(r["value"][1]) for r in result) * scale


def classify(pod, app_prefix):
    if pod.startswith(app_prefix):
        return "app"
    for name, prefixes in CLASSES:
        if pod.startswith(prefixes):
            return name
    return "other"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--prom", required=True)
    ap.add_argument("--ns", required=True)
    ap.add_argument("--app-prefix", required=True)
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--target", required=True)
    ap.add_argument("--idle-start-ms", type=float, default=None)
    ap.add_argument("--idle-end-ms", type=float, default=None)
    args = ap.parse_args()

    files = sorted(glob.glob(f"{args.run_dir}/journeys-*.json"))
    if not files:
        sys.exit(f"no journeys-*.json in {args.run_dir}")
    jdoc = json.load(open(files[-1]))
    window = jdoc.get("window")
    if not window:
        sys.exit("journeys block has no window (harness older than v1?)")
    start, end = window["start_ms"] / 1000.0, window["end_ms"] / 1000.0
    w = max(1, int(round(end - start)))
    # cAdvisor is scraped every ~15–30 s: give the last scrape of the window time to land before reading it.
    lag = end + 45 - time.time()
    if lag > 0:
        time.sleep(lag)

    ns, rng = args.ns, f"[{w}s]"
    sel = f'namespace="{ns}",container=""'
    cpu = by_pod(query(args.prom, f"sum by (pod) (increase(container_cpu_usage_seconds_total{{{sel}}}{rng}))", end))
    mem_avg = by_pod(query(args.prom, f"sum by (pod) (avg_over_time(container_memory_working_set_bytes{{{sel}}}[{w}s:15s]))", end), 1 / 2**20)
    mem_max = by_pod(query(args.prom, f"sum by (pod) (max_over_time(container_memory_working_set_bytes{{{sel}}}[{w}s:15s]))", end), 1 / 2**20)
    ingress_cpu = scalar(query(args.prom, f'sum(increase(container_cpu_usage_seconds_total{{namespace="ingress-nginx",container=""}}{rng}))', end))
    node_busy = scalar(query(args.prom, f'sum(rate(node_cpu_seconds_total{{mode!~"idle|iowait|steal"}}{rng}))', end))

    journeys = (jdoc.get("totals") or {}).get("http_journeys") or sum(
        v["count"] for k, v in jdoc.get("journeys", {}).items() if k != "J8")
    per_j = (lambda core_s: round(core_s * 1000 / journeys, 4)) if journeys else (lambda core_s: None)

    pods = {}
    for pod in sorted(set(cpu) | set(mem_avg)):
        pods[pod] = {
            "class": classify(pod, args.app_prefix),
            "cpu_core_s": round(cpu.get(pod, 0.0), 3),
            "mem_avg_mib": round(mem_avg.get(pod, 0.0), 1),
            "mem_max_mib": round(mem_max.get(pod, 0.0), 1),
        }
    classes = {}
    for pod, v in pods.items():
        c = classes.setdefault(v["class"], {"pods": 0, "cpu_core_s": 0.0, "mem_avg_mib": 0.0, "mem_max_mib": 0.0})
        c["pods"] += 1
        for k in ("cpu_core_s", "mem_avg_mib", "mem_max_mib"):
            c[k] += v[k]
    namespace = {"pods": len(pods), "cpu_core_s": 0.0, "mem_avg_mib": 0.0, "mem_max_mib": 0.0}
    for c in classes.values():
        c["cpu_ms_per_journey"] = per_j(c["cpu_core_s"])
        c["avg_cores"] = round(c["cpu_core_s"] / w, 4)
        for k in ("cpu_core_s", "mem_avg_mib", "mem_max_mib"):
            namespace[k] += c[k]
            c[k] = round(c[k], 3)
    namespace = {k: round(v, 3) if isinstance(v, float) else v for k, v in namespace.items()}
    namespace["cpu_ms_per_journey"] = per_j(namespace["cpu_core_s"])
    namespace["avg_cores"] = round(namespace["cpu_core_s"] / w, 4)

    idle = None
    if args.idle_start_ms and args.idle_end_ms and args.idle_end_ms > args.idle_start_ms:
        i_end = args.idle_end_ms / 1000.0
        i_w = max(1, int(round(i_end - args.idle_start_ms / 1000.0)))
        lag = i_end + 45 - time.time()
        if lag > 0:
            time.sleep(lag)
        i_cpu = by_pod(query(args.prom, f"sum by (pod) (increase(container_cpu_usage_seconds_total{{{sel}}}[{i_w}s]))", i_end))
        i_classes = {}
        for pod, core_s in i_cpu.items():
            c = i_classes.setdefault(classify(pod, args.app_prefix), {"pods": 0, "cpu_core_s": 0.0})
            c["pods"] += 1
            c["cpu_core_s"] += core_s
        i_total = 0.0
        for c in i_classes.values():
            c["avg_cores"] = round(c["cpu_core_s"] / i_w, 4)
            c["cpu_core_s"] = round(c["cpu_core_s"], 3)
            i_total += c["avg_cores"]
        i_ingress = scalar(query(args.prom, f'sum(increase(container_cpu_usage_seconds_total{{namespace="ingress-nginx",container=""}}[{i_w}s]))', i_end))
        idle = {"start_ms": args.idle_start_ms, "end_ms": args.idle_end_ms, "window_s": i_w,
                "classes": i_classes, "namespace_avg_cores": round(i_total, 4),
                "ingress_nginx_avg_cores": round(i_ingress / i_w, 4)}
        # Marginal cost = measured CPU minus the standing cost the same pods burn with no traffic.
        for name, c in classes.items():
            idle_cores = (i_classes.get(name) or {}).get("avg_cores", 0.0)
            c["idle_avg_cores"] = idle_cores
            c["marginal_cpu_ms_per_journey"] = per_j(max(0.0, c["cpu_core_s"] - idle_cores * w))
        namespace["idle_avg_cores"] = idle["namespace_avg_cores"]
        namespace["marginal_cpu_ms_per_journey"] = per_j(max(0.0, namespace["cpu_core_s"] - idle["namespace_avg_cores"] * w))

    out = {
        "target": args.target, "namespace": ns, "window": window, "window_s": w,
        "http_journeys": journeys,
        "pods": pods, "classes": classes, "namespace_total": namespace,
        "ingress_nginx": {"cpu_core_s": round(ingress_cpu, 3), "cpu_ms_per_journey": per_j(ingress_cpu)},
        "node_busy_cores_avg": round(node_busy, 3),
        "idle": idle,
        "queried_at": time.time(),
    }
    json.dump(out, open(f"{args.run_dir}/resources.json", "w"), indent=2)
    app = classes.get("app", {})
    print(f"  window {w}s, {journeys} journeys: app {app.get('cpu_core_s', 0):.1f} core-s "
          f"({app.get('cpu_ms_per_journey')} ms/journey, mem avg {app.get('mem_avg_mib', 0):.0f} MiB) | "
          f"namespace {namespace['cpu_ms_per_journey']} ms/journey | ingress {out['ingress_nginx']['cpu_ms_per_journey']} ms/journey"
          + (f" | idle app {app.get('idle_avg_cores')} cores → marginal {app.get('marginal_cpu_ms_per_journey')} ms/journey" if idle else ""))


if __name__ == "__main__":
    main()
