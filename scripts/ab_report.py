#!/usr/bin/env python3
"""Aggregates one A/B round archive (scripts/ab-round.sh) → round-summary.json + REPORT.md.

Only VALID runs (run.json "valid": true) enter the statistics; invalid ones are listed with their reasons.
For every (profile, target) and metric: mean, sample SD and the 95 % confidence half-width (Student t, n-1 dof)
across the repeats — the repeat is the experimental unit (each run starts from a reseeded dataset and fresh pods).
Comparison columns show B/A ratios; "CIs overlap" flags differences the round cannot distinguish from noise.

Usage: ab_report.py <round-dir>
"""
import glob
import json
import math
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ab_stats  # noqa: E402  (pure-Python Welch / Mann-Whitney / Cliff / Holm)

T975 = {1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571, 6: 2.447, 7: 2.365, 8: 2.306, 9: 2.262, 10: 2.228,
        11: 2.201, 12: 2.179, 13: 2.160, 14: 2.145, 15: 2.131, 16: 2.120, 17: 2.110, 18: 2.101, 19: 2.093,
        20: 2.086, 25: 2.060, 30: 2.042}
JOURNEY_ORDER = ["J1", "J2", "J3", "J5", "J6", "J7", "nav", "J4", "J9"]
JOURNEY_NAMES = {"J1": "space feed", "J2": "thread open", "J3": "comment create", "J4": "thread create",
                 "J5": "reaction toggle", "J6": "search", "J7": "profile", "J9": "DM", "nav": "list spaces"}


def t975(dof):
    if dof <= 0:
        return float("nan")
    keys = sorted(k for k in T975 if k <= dof)
    return T975[keys[-1]] if dof <= 30 else 1.96


def stat(values):
    values = [v for v in values if v is not None and not (isinstance(v, float) and math.isnan(v))]
    if not values:
        return None
    n = len(values)
    mean = statistics.mean(values)
    sd = statistics.stdev(values) if n > 1 else 0.0
    ci = t975(n - 1) * sd / math.sqrt(n) if n > 1 else None
    return {"n": n, "mean": mean, "sd": sd, "ci95": ci, "min": min(values), "max": max(values), "values": values}


def load(path):
    try:
        return json.load(open(path))
    except (OSError, ValueError):
        return None


def newest(run_dir, pattern):
    files = sorted(glob.glob(os.path.join(run_dir, pattern)))
    return load(files[-1]) if files else None


def k6_load(samples):
    """Max / mean CPU% of the k6 process (from cumulative /proc ticks) and max RSS during the run."""
    if not samples:
        return None
    pts = [(s.get("epoch"), s.get("k6_cpu_ticks"), s.get("clk_tck") or 100) for s in samples]
    pts = [p for p in pts if p[0] is not None and p[1] is not None]
    pct = []
    for (e0, t0, hz), (e1, t1, _) in zip(pts, pts[1:]):
        if e1 > e0 and t1 >= t0:
            pct.append((t1 - t0) / hz / (e1 - e0) * 100)
    rss = [s.get("k6_rss_mib") for s in samples if s.get("k6_rss_mib") is not None]
    return {"cpu_pct_max": max(pct) if pct else None, "cpu_pct_mean": statistics.mean(pct) if pct else None,
            "rss_mib_max": max(rss) if rss else None}


def collect(round_dir):
    runs = []
    for run_json in sorted(glob.glob(os.path.join(round_dir, "*", "*", "run-*", "run.json"))):
        run_dir = os.path.dirname(run_json)
        meta = load(run_json) or {}
        meta["dir"] = os.path.relpath(run_dir, round_dir)
        meta["journeys_doc"] = newest(run_dir, "journeys-*.json")
        meta["resources"] = load(os.path.join(run_dir, "resources.json"))
        meta["k6"] = k6_load(newest(run_dir, "samples-*.json"))
        runs.append(meta)
    return runs


def aggregate(runs):
    out = {}
    for r in runs:
        if not r.get("valid") or not r.get("journeys_doc"):
            continue
        key = (r["profile"], r["target"])
        out.setdefault(key, []).append(r)
    summary = {}
    for (profile, target), rs in out.items():
        jd = [r["journeys_doc"] for r in rs]
        entry = {"runs": len(rs), "journeys": {}, "totals": {}, "resources": {}, "k6": {}, "ws": {}}
        names = sorted({j for d in jd for j in d.get("journeys", {})})
        for j in names:
            rows = [d["journeys"].get(j) for d in jd if d["journeys"].get(j)]
            entry["journeys"][j] = {
                "per_second": stat([x["per_second"] for x in rows]),
                "med_ms": stat([x["duration_ms"]["med"] for x in rows]),
                "p95_ms": stat([x["duration_ms"]["p95"] for x in rows]),
                "p99_ms": stat([x["duration_ms"]["p99"] for x in rows]),
                "count": stat([x["count"] for x in rows]),
            }
        tot = [d.get("totals", {}) for d in jd]
        entry["totals"] = {
            "http_journeys_per_second": stat([t.get("http_journeys_per_second") for t in tot]),
            "journey_p95_ms": stat([(t.get("journey_duration_ms") or {}).get("p95") for t in tot]),
            "journey_med_ms": stat([(t.get("journey_duration_ms") or {}).get("med") for t in tot]),
            "http_req_p95_ms": stat([t.get("http_req_p95_ms") for t in tot]),
            "http_req_failed_pct": stat([t.get("http_req_failed_pct") for t in tot]),
            "http_reqs": stat([t.get("http_reqs") for t in tot]),
            "kib_received_per_journey": stat([t.get("kib_received_per_journey") for t in tot]),
        }
        entry["ws"] = {k: stat([(t.get("ws") or {}).get(k) for t in tot]) for k in ("subscribed", "notifications", "errors")}
        # J8 receipt probes (harness h4): latency from the write's HTTP response to the matching push, and loss.
        entry["ws"].update({
            "probes_sent": stat([(t.get("ws") or {}).get("probes_sent") for t in tot]),
            "probes_received": stat([(t.get("ws") or {}).get("probes_received") for t in tot]),
            "probe_loss_pct": stat([(t.get("ws") or {}).get("probe_loss_pct") for t in tot]),
            "probe_late_pct": stat([(t.get("ws") or {}).get("probe_late_pct") for t in tot]),
            "receipt_med_ms": stat([((t.get("ws") or {}).get("receipt_ms") or {}).get("med") for t in tot]),
            "receipt_p95_ms": stat([((t.get("ws") or {}).get("receipt_ms") or {}).get("p95") for t in tot]),
            "receipt_max_ms": stat([((t.get("ws") or {}).get("receipt_ms") or {}).get("max") for t in tot]),
        })
        res = [r["resources"] for r in rs if r.get("resources")]
        if res:
            def cls(c, k):
                return stat([(x["classes"].get(c) or {}).get(k) for x in res])
            entry["resources"] = {
                "app_cpu_ms_per_journey": cls("app", "cpu_ms_per_journey"),
                "app_marginal_cpu_ms_per_journey": cls("app", "marginal_cpu_ms_per_journey"),
                "app_idle_avg_cores": cls("app", "idle_avg_cores"),
                "app_avg_cores": cls("app", "avg_cores"),
                "app_mem_avg_mib": cls("app", "mem_avg_mib"),
                "app_mem_max_mib": cls("app", "mem_max_mib"),
                "db_cpu_ms_per_journey": cls("db", "cpu_ms_per_journey"),
                "db_idle_avg_cores": cls("db", "idle_avg_cores"),
                "db_mem_avg_mib": cls("db", "mem_avg_mib"),
                "bus_cpu_ms_per_journey": cls("bus", "cpu_ms_per_journey"),
                "bus_idle_avg_cores": cls("bus", "idle_avg_cores"),
                "bus_mem_avg_mib": cls("bus", "mem_avg_mib"),
                "frontend_mem_avg_mib": cls("frontend", "mem_avg_mib"),
                "ns_cpu_ms_per_journey": stat([x["namespace_total"]["cpu_ms_per_journey"] for x in res]),
                "ns_marginal_cpu_ms_per_journey": stat([x["namespace_total"].get("marginal_cpu_ms_per_journey") for x in res]),
                "ns_idle_avg_cores": stat([x["namespace_total"].get("idle_avg_cores") for x in res]),
                "ns_mem_avg_mib": stat([x["namespace_total"]["mem_avg_mib"] for x in res]),
                "ingress_cpu_ms_per_journey": stat([x["ingress_nginx"]["cpu_ms_per_journey"] for x in res]),
                "node_busy_cores_avg": stat([x.get("node_busy_cores_avg") for x in res]),
            }
        k6 = [r["k6"] for r in rs if r.get("k6")]
        entry["k6"] = {"cpu_pct_max": stat([x["cpu_pct_max"] for x in k6]), "rss_mib_max": stat([x["rss_mib_max"] for x in k6])}
        caps = [d.get("capacity") for d in jd if d.get("capacity")]
        if caps:
            steps = {}
            for c in caps:
                for s in c["steps"]:
                    st = steps.setdefault(s["step"], {"offered_jps": s["offered_jps"], "achieved": [], "p95": [], "p99": [], "failed": []})
                    st["achieved"].append(s["achieved_jps"])
                    st["p95"].append(s["duration_ms"]["p95"])
                    st["p99"].append(s["duration_ms"]["p99"])
                    st["failed"].append(s["failed_pct"])
            entry["capacity"] = {
                "sustained_jps": stat([c.get("sustained_jps") for c in caps]),
                "knees": [c.get("knee") for c in caps],
                "dropped_iterations": stat([c.get("dropped_iterations") for c in caps]),
                "steps": {k: {"offered_jps": v["offered_jps"], "achieved_jps": stat(v["achieved"]),
                              "p95_ms": stat(v["p95"]), "p99_ms": stat(v["p99"]), "failed_pct": stat(v["failed"])}
                          for k, v in sorted(steps.items())},
            }
        summary[f"{profile}/{target}"] = entry
    return summary


def fmt(s, digits=1, ci=True):
    if not s:
        return "–"
    v = f"{s['mean']:.{digits}f}"
    if ci and s.get("ci95") is not None and s["n"] > 1:
        v += f" ± {s['ci95']:.{digits}f}"
    return v


def ratio(a, b):
    if not a or not b or not a["mean"]:
        return "–"
    return f"{b['mean'] / a['mean']:.2f}×"


def overlap(a, b):
    if not a or not b or a.get("ci95") is None or b.get("ci95") is None:
        return ""
    lo_a, hi_a = a["mean"] - a["ci95"], a["mean"] + a["ci95"]
    lo_b, hi_b = b["mean"] - b["ci95"], b["mean"] + b["ci95"]
    return "overlap" if lo_a <= hi_b and lo_b <= hi_a else "distinct"


def variants_label(rnd):
    """The round's recorded variant flags (hpa, gc_variant, relay_poll_ms, auth_reads, real_spa) as one cell."""
    variants = rnd.get("variants") or {}
    flags = [f"{k}={v}" for k, v in variants.items() if k != "k6_env" and v not in (False, None)]
    return ", ".join(flags) if flags else "none (default harness)"


def table(rows, header):
    lines = ["| " + " | ".join(header) + " |", "|" + "|".join("---" for _ in header) + "|"]
    lines += ["| " + " | ".join(r) + " |" for r in rows]
    return "\n".join(lines)


def report(round_dir, runs, summary):
    rnd = load(os.path.join(round_dir, "round.json")) or {}
    ma = load(os.path.join(round_dir, "meta-A.json")) or {}
    mb = load(os.path.join(round_dir, "meta-B.json")) or {}
    md = [f"# A/B measurement round `{os.path.basename(round_dir)}`", ""]
    md.append("Generated by `scripts/ab_report.py` from the raw archive in this directory — do not edit by hand; "
              "re-run the script instead. Statistics: mean ± 95 % CI (Student t over the valid repeats; every repeat "
              "starts from a reseeded dataset and fresh pods). Ratios are B/A. *distinct* = the two 95 % CIs do not "
              "overlap; *overlap* = this round cannot separate the two means.")
    md.append("")
    md.append(table([
        ["plan", rnd.get("plan", "?"), "order", rnd.get("order", "?")],
        ["mix", rnd.get("mix_version", "?"), "harness", rnd.get("harness_version", "?")],
        ["A image", f"`{ma.get('deployed_image', '?')}`", "A git", f"{ma.get('git_branch', '?')} @ {str(ma.get('git_sha', '?'))[:9]}{' (dirty)' if ma.get('git_dirty') else ''}"],
        ["B image", f"`{mb.get('deployed_image', '?')}`", "B git", f"{mb.get('git_branch', '?')} @ {str(mb.get('git_sha', '?'))[:9]}{' (dirty)' if mb.get('git_dirty') else ''}"],
        ["replicas", str(rnd.get("replicas", "?")), "reseed / warm-up each run", f"{rnd.get('reseed_each_run')} / {rnd.get('warmup_each_run')}"],
        ["variants", variants_label(rnd), "k6 env", " ".join((rnd.get("variants") or {}).get("k6_env") or []) or "—"],
    ], ["", "", "", ""]))
    md.append("")
    if (rnd.get("variants") or {}).get("real_spa"):
        md.append("**A = the real SPA client** (`--real-spa`, deviation D1): A's read journeys replay the React pages' "
                  "cold-cache request sets (icons, avatars, author stats, related search, attachment rail — "
                  "`load/k6/targets/README.md`), not the idealized core calls of the default rounds. Compare with a "
                  "default round of the same plan to read A's cost as a lower/upper bound; B is identical in both.")
        md.append("")

    md.append("## Run inventory")
    inv = []
    for r in runs:
        inv.append([r.get("profile", "?"), {"a": "A", "b": "B"}.get(r.get("target"), "?"), str(r.get("repeat", "?")),
                    "valid" if r.get("valid") else "**INVALID**", "; ".join(r.get("invalid_reasons", [])) or "",
                    f"`{r['dir']}`"])
    md.append(table(inv, ["profile", "target", "repeat", "status", "reasons", "archive"]))
    md.append("")

    profiles = []
    for key in summary:
        p = key.split("/")[0]
        if p not in profiles:
            profiles.append(p)
    for p in profiles:
        a, b = summary.get(f"{p}/a"), summary.get(f"{p}/b")
        md.append(f"## Profile `{p}` — A: {a['runs'] if a else 0} valid run(s), B: {b['runs'] if b else 0}")
        md.append("")
        if not a or not b:
            md.append("_Only one architecture has valid runs for this profile — no comparison._")
            md.append("")
        ta, tb = (a or {}).get("totals", {}), (b or {}).get("totals", {})
        md.append("### Throughput, errors, overall latency")
        rows = []
        for label, key, d in (("HTTP journeys / s (scenario window)", "http_journeys_per_second", 2),
                              ("journey latency median (ms)", "journey_med_ms", 1),
                              ("journey latency p95 (ms)", "journey_p95_ms", 1),
                              ("HTTP request p95 (ms)", "http_req_p95_ms", 1),
                              ("HTTP requests per run", "http_reqs", 0),
                              ("KiB received per journey (wire: gzip + TLS)", "kib_received_per_journey", 2),
                              ("failed requests (%)", "http_req_failed_pct", 2)):
            rows.append([label, fmt(ta.get(key), d), fmt(tb.get(key), d), ratio(ta.get(key), tb.get(key)), overlap(ta.get(key), tb.get(key))])
        md.append(table(rows, ["metric", "A", "B", "B/A", "95 % CIs"]))
        md.append("")
        md.append("Closed-model profiles fix the offered load (VUs × think time), so journeys/s is near-equal by design; "
                  "the comparison lives in latency and in the resources each architecture spends on the same work.")
        md.append("")

        md.append("### Per-journey latency (ms)")
        rows = []
        for j in JOURNEY_ORDER:
            ja = ((a or {}).get("journeys") or {}).get(j)
            jb = ((b or {}).get("journeys") or {}).get(j)
            if not ja and not jb:
                continue
            for q in ("med_ms", "p95_ms", "p99_ms"):
                sa, sb = (ja or {}).get(q), (jb or {}).get(q)
                rows.append([f"{j} {JOURNEY_NAMES.get(j, '')}" if q == "med_ms" else "", q.replace("_ms", ""),
                             fmt(sa), fmt(sb), ratio(sa, sb), overlap(sa, sb)])
        md.append(table(rows, ["journey", "stat", "A", "B", "B/A", "95 % CIs"]))
        md.append("")

        ra, rb = (a or {}).get("resources", {}), (b or {}).get("resources", {})
        if ra or rb:
            md.append("### Resources over the scenario window (H1)")
            rows = []
            for label, key, d in (("app CPU per journey (ms)", "app_cpu_ms_per_journey", 3),
                                  ("app MARGINAL CPU per journey (ms, idle cost removed)", "app_marginal_cpu_ms_per_journey", 3),
                                  ("app idle cores (no traffic, all replicas)", "app_idle_avg_cores", 3),
                                  ("app avg cores", "app_avg_cores", 3),
                                  ("app memory avg (MiB, all replicas)", "app_mem_avg_mib", 0),
                                  ("app memory max (MiB, all replicas)", "app_mem_max_mib", 0),
                                  ("DB CPU per journey (ms)", "db_cpu_ms_per_journey", 3),
                                  ("DB idle cores (no traffic)", "db_idle_avg_cores", 3),
                                  ("DB memory avg (MiB)", "db_mem_avg_mib", 0),
                                  ("bus CPU per journey (ms) — A RabbitMQ / B Redis", "bus_cpu_ms_per_journey", 3),
                                  ("bus idle cores (no traffic)", "bus_idle_avg_cores", 3),
                                  ("bus memory avg (MiB)", "bus_mem_avg_mib", 0),
                                  ("A frontend pod memory avg (MiB)", "frontend_mem_avg_mib", 0),
                                  ("WHOLE namespace CPU per journey (ms)", "ns_cpu_ms_per_journey", 3),
                                  ("WHOLE namespace MARGINAL CPU per journey (ms)", "ns_marginal_cpu_ms_per_journey", 3),
                                  ("WHOLE namespace idle cores (no traffic)", "ns_idle_avg_cores", 3),
                                  ("WHOLE namespace memory avg (MiB)", "ns_mem_avg_mib", 0),
                                  ("ingress-nginx CPU per journey (ms, shared edge)", "ingress_cpu_ms_per_journey", 3),
                                  ("node busy cores avg (context)", "node_busy_cores_avg", 2)):
                rows.append([label, fmt(ra.get(key), d), fmt(rb.get(key), d), ratio(ra.get(key), rb.get(key)), overlap(ra.get(key), rb.get(key))])
            md.append(table(rows, ["metric", "A", "B", "B/A", "95 % CIs"]))
            md.append("")

        wa, wb = (a or {}).get("ws", {}), (b or {}).get("ws", {})
        if (wa.get("subscribed") or {}).get("mean") or (wb.get("subscribed") or {}).get("mean"):
            md.append("### WebSocket holds (J8) — semantics differ (A: per-view subscription; B: broadcast to every socket)")
            rows = [[k, fmt(wa.get(k), 0), fmt(wb.get(k), 0)] for k in ("subscribed", "notifications", "errors")]
            if (wa.get("probes_sent") or {}).get("mean") or (wb.get("probes_sent") or {}).get("mean"):
                rows += [["receipt probes sent (comment → own push)", fmt(wa.get("probes_sent"), 0), fmt(wb.get("probes_sent"), 0)],
                         ["receipt probes received", fmt(wa.get("probes_received"), 0), fmt(wb.get("probes_received"), 0)],
                         ["probe loss (%, never pushed within the hold)", fmt(wa.get("probe_loss_pct"), 2), fmt(wb.get("probe_loss_pct"), 2)],
                         ["probe late (%, pushed after the timeout)", fmt(wa.get("probe_late_pct"), 2), fmt(wb.get("probe_late_pct"), 2)],
                         ["receipt latency median (ms)", fmt(wa.get("receipt_med_ms"), 0), fmt(wb.get("receipt_med_ms"), 0)],
                         ["receipt latency p95 (ms)", fmt(wa.get("receipt_p95_ms"), 0), fmt(wb.get("receipt_p95_ms"), 0)],
                         ["receipt latency max (ms)", fmt(wa.get("receipt_max_ms"), 0), fmt(wb.get("receipt_max_ms"), 0)]]
            md.append(table(rows, ["metric", "A", "B"]))
            md.append("")

        ka, kb = (a or {}).get("k6", {}), (b or {}).get("k6", {})
        md.append(f"Load generator (k6 on the same host): max CPU A {fmt(ka.get('cpu_pct_max'), 0, False)} % / "
                  f"B {fmt(kb.get('cpu_pct_max'), 0, False)} % of one core, max RSS A {fmt(ka.get('rss_mib_max'), 0, False)} MiB / "
                  f"B {fmt(kb.get('rss_mib_max'), 0, False)} MiB.")
        md.append("")

        ca, cb = (a or {}).get("capacity"), (b or {}).get("capacity")
        if ca or cb:
            md.append("### Capacity (open model): offered vs achieved journeys/s per step (H2)")
            steps = sorted(set((ca or {}).get("steps", {}).keys()) | set((cb or {}).get("steps", {}).keys()))
            rows = []
            for s in steps:
                sa = (ca or {}).get("steps", {}).get(s, {})
                sb = (cb or {}).get("steps", {}).get(s, {})
                rows.append([s, str(sa.get("offered_jps") or sb.get("offered_jps")),
                             fmt(sa.get("achieved_jps")), fmt(sa.get("p95_ms"), 0), fmt(sa.get("failed_pct"), 2),
                             fmt(sb.get("achieved_jps")), fmt(sb.get("p95_ms"), 0), fmt(sb.get("failed_pct"), 2)])
            md.append(table(rows, ["step", "offered j/s", "A achieved", "A p95 ms", "A failed %", "B achieved", "B p95 ms", "B failed %"]))
            md.append("")
            md.append(f"**Sustained throughput** (highest step before the knee: p95 > 500 ms, failures > 1 % or achieved "
                      f"< 95 % of offered): A {fmt((ca or {}).get('sustained_jps'), 0)} j/s, "
                      f"B {fmt((cb or {}).get('sustained_jps'), 0)} j/s. Knees: A {(ca or {}).get('knees')}, B {(cb or {}).get('knees')}.")
            md.append("")

    tests = ab_stats.run_round_tests(summary)
    if any(fam["headline"] or fam["journeys"] for fam in tests.values()):
        md.append("## Statistical tests (per-repeat values; the repeat is the experimental unit)")
        md.append("Welch's t-test (two-sided), exact Mann-Whitney U, Cliff's δ (effect size, positive = B larger) and "
                  "Holm-Bonferroni adjusted p over each family of metrics (`scripts/ab_stats.py`). Verdicts: *distinct* = "
                  "Holm-adjusted p < 0.05; *raw-only* = only the unadjusted p < 0.05 (suggestive); *no difference detected* "
                  "≠ equal (no equivalence test is run); *n<3* = too few repeats. With 5 vs 5 the smallest exact "
                  "Mann-Whitney p is 0.0079 (complete separation of the ten values).")
        md.append("")
        md.append(ab_stats.markdown_tables(tests))
    md.append("## Reading guide and known limits")
    md.append("See `thesis/results/AB-COMPARISON-v1.md` for the methodology, the fairness invariants and the deviation "
              "register (§6) that every number in this report carries. In short: A's adapter is an idealized SPA "
              "client (D1), B renders a body preview per feed/search card while A's cards carry none (D2), read journeys "
              "are anonymous (D3), J6 matches nearly every thread on both sides (D4).")
    return "\n".join(md) + "\n"


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    round_dir = os.path.abspath(sys.argv[1])
    runs = collect(round_dir)
    if not runs:
        sys.exit(f"no runs found under {round_dir}")
    summary = aggregate(runs)
    json.dump({"round": os.path.basename(round_dir), "runs": [{k: v for k, v in r.items() if k not in ("journeys_doc", "resources")} for r in runs],
               "summary": summary}, open(os.path.join(round_dir, "round-summary.json"), "w"), indent=2)
    open(os.path.join(round_dir, "REPORT.md"), "w").write(report(round_dir, runs, summary))
    json.dump(ab_stats.run_round_tests(summary), open(os.path.join(round_dir, "stats.json"), "w"), indent=2)
    valid = sum(1 for r in runs if r.get("valid"))
    print(f"{len(runs)} runs ({valid} valid) → round-summary.json + REPORT.md")


if __name__ == "__main__":
    main()
