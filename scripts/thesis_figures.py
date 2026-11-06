#!/usr/bin/env python3
"""Render the thesis figures (Polish labels, units on every axis) from an archived A/B round.

Reads only committed archive files:
  <round>/round-summary.json            (per-profile aggregates with per-repeat values)
  <campaign>/03-round/capacity-per-step-cpu.json   (optional: DB/app cores per capacity step)
  <lighthouse-dir>/lighthouse-metrics-cluster-{a,b}-{mobile,desktop}.txt  (optional)
  <chaos-dir>/*/CHAOS-REPORT.md         (optional: failed requests + recovery per fault)

Writes PNG (200 dpi) + SVG per figure and a README.md that maps every figure to its data
source and a suggested caption, so the results chapter can cite them (Rys. 1, Rys. 2, ...).

Usage:
  python3 scripts/thesis_figures.py --round thesis/results/rounds/20261018-0051-v1 \
      --capacity-cpu thesis/results/campaign-20261017-2357/03-round/capacity-per-step-cpu.json \
      --lighthouse thesis/results/lighthouse-cluster-20261018 --chaos thesis/results/chaos \
      --out thesis/figures/v1
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

A_COLOR = "#2a78d6"
B_COLOR = "#eb6834"
GRID = "#dde1e8"
JOURNEY_LABELS = {
    "J1": "J1 feed",
    "J2": "J2 wątek",
    "J3": "J3 komentarz",
    "J4": "J4 wątek+zdjęcie",
    "J5": "J5 reakcja",
    "J6": "J6 wyszukiwanie",
    "J7": "J7 profil",
    "J9": "J9 wiadomość",
    "nav": "nav lista przestrzeni",
}

plt.rcParams.update(
    {
        "font.family": "DejaVu Sans",
        "font.size": 10,
        "axes.spines.top": False,
        "axes.spines.right": False,
        "axes.grid": True,
        "grid.color": GRID,
        "grid.linewidth": 0.8,
        "axes.axisbelow": True,
        "legend.frameon": False,
        "figure.dpi": 100,
        "savefig.dpi": 200,
        "savefig.bbox": "tight",
    }
)

README: list[tuple[str, str, str]] = []  # (file, source, caption)


def save(fig, out_dir: str, name: str, source: str, caption: str) -> None:
    for ext in ("png", "svg"):
        fig.savefig(os.path.join(out_dir, f"{name}.{ext}"))
    plt.close(fig)
    README.append((name, source, caption))
    print(f"  wrote {name}.png/.svg")


def m(d: dict | None, key: str = "mean", default=None):
    if not d:
        return default
    v = d.get(key)
    return default if v is None else v


def fig_latency_per_journey(summary: dict, profile: str, out: str, n_label: str) -> None:
    a, b = summary.get(f"{profile}/a"), summary.get(f"{profile}/b")
    if not a or not b:
        return
    js = [j for j in JOURNEY_LABELS if j in a["journeys"] and j in b["journeys"] and j != "J8"]
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2), sharey=False)
    for ax, stat, title in zip(axes, ("med_ms", "p95_ms"), ("mediana", "p95")):
        ya = [m(a["journeys"][j][stat]) for j in js]
        yb = [m(b["journeys"][j][stat]) for j in js]
        ea = [m(a["journeys"][j][stat], "ci95", 0) or 0 for j in js]
        eb = [m(b["journeys"][j][stat], "ci95", 0) or 0 for j in js]
        x = range(len(js))
        w = 0.38
        ax.bar([i - w / 2 for i in x], ya, w, yerr=ea, color=A_COLOR, label="A — SPA + .NET", capsize=2)
        ax.bar([i + w / 2 for i in x], yb, w, yerr=eb, color=B_COLOR, label="B — Go SSR", capsize=2)
        ax.set_xticks(list(x))
        ax.set_xticklabels([JOURNEY_LABELS[j] for j in js], rotation=30, ha="right")
        ax.set_ylabel("czas odpowiedzi podróży [ms]")
        ax.set_title(f"Opóźnienie na podróż — {title} ({profile}, {n_label})")
    axes[0].legend(loc="upper left")
    fig.tight_layout()
    save(
        fig,
        out,
        f"latency-per-journey-{profile}",
        f"round-summary.json → summary.{profile}/{{a,b}}.journeys.*.{{med_ms,p95_ms}}",
        f"Opóźnienie podróży użytkownika w profilu {profile}: mediana (lewy panel) i 95. percentyl (prawy panel) "
        f"w milisekundach, średnia z powtórzeń ({n_label}), słupki błędu = 95 % przedział ufności. A = niebieski, B = pomarańczowy.",
    )


def fig_capacity(summary: dict, out: str) -> None:
    a, b = summary.get("capacity/a"), summary.get("capacity/b")
    if not a or not b:
        return
    steps = sorted(a["capacity"]["steps"].keys())
    off = [a["capacity"]["steps"][s]["offered_jps"] for s in steps]
    acha = [m(a["capacity"]["steps"][s]["achieved_jps"]) for s in steps]
    achb = [m(b["capacity"]["steps"][s]["achieved_jps"]) for s in steps]
    # h6 archives carry the core p95 (every journey except J4 — the knee's own rule); older ones only the all-journey p95.
    def step_p95(side, s):
        st = side["capacity"]["steps"][s]
        return m(st["core_p95_ms"] if st.get("core_p95_ms") else st["p95_ms"])
    p95a = [step_p95(a, s) for s in steps]
    p95b = [step_p95(b, s) for s in steps]

    fig, ax = plt.subplots(figsize=(6.4, 4.4))
    ax.plot([0, max(off)], [0, max(off)], ls="--", color="#737b8a", lw=1, label="obsłużono wszystko")
    ax.plot(off, acha, marker="o", color=A_COLOR, label="A — SPA + .NET")
    ax.plot(off, achb, marker="s", color=B_COLOR, label="B — Go SSR")
    ax.set_xlabel("zadana częstość podróży [podróże/s]")
    ax.set_ylabel("osiągnięta częstość podróży [podróże/s]")
    ax.set_title("Model otwarty (capacity): osiągnięte vs zadane")
    ax.legend(loc="upper left")
    fig.tight_layout()
    save(
        fig,
        out,
        "capacity-offered-vs-achieved",
        "round-summary.json → summary.capacity/{a,b}.capacity.steps.*.achieved_jps",
        "Przepustowość w modelu otwartym: osiągnięta częstość podróży [podróże/s] w funkcji zadanej, osiem kroków po 60 s "
        "(1 powtórzenie na stronę). Linia przerywana = pełna obsługa zadanego ruchu.",
    )

    fig, ax = plt.subplots(figsize=(6.4, 4.4))
    ax.axhline(500, ls="--", color="#b4322f", lw=1.2, label="próg kolana p95 = 500 ms")
    ax.plot(off, p95a, marker="o", color=A_COLOR, label="A — SPA + .NET")
    ax.plot(off, p95b, marker="s", color=B_COLOR, label="B — Go SSR")
    ax.set_yscale("log")
    ax.set_xlabel("zadana częstość podróży [podróże/s]")
    ax.set_ylabel("p95 czasu odpowiedzi podróży [ms, skala log]")
    ax.set_title("Model otwarty (capacity): p95 na krok")
    ax.legend(loc="upper left")
    fig.tight_layout()
    save(
        fig,
        out,
        "capacity-p95-per-step",
        "round-summary.json → summary.capacity/{a,b}.capacity.steps.*.p95_ms",
        "95. percentyl czasu odpowiedzi podróży [ms, skala logarytmiczna] na każdym kroku profilu capacity; "
        "czerwona linia = próg 500 ms definiujący kolano przepustowości (H2).",
    )


def fig_capacity_cpu(path: str, out: str) -> None:
    if not path or not os.path.exists(path):
        return
    c = json.load(open(path))
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2), sharey=True)
    for ax, key, title in zip(axes, ("db_cores", "app_cores_2_replicas"), ("baza danych (1 pod)", "aplikacja (2 repliki)")):
        for side, color, marker, label in (("A", A_COLOR, "o", "A — SPA + .NET"), ("B", B_COLOR, "s", "B — Go SSR")):
            rows = c[side]
            ax.plot([r["offered_jps"] for r in rows], [r[key] for r in rows], marker=marker, color=color, label=label)
        lim = 1.0 if key == "db_cores" else 2.0
        ax.axhline(lim, ls="--", color="#b4322f", lw=1.2, label=f"limit {lim:g} CPU")
        ax.set_xlabel("zadana częstość podróży [podróże/s]")
        ax.set_title(f"CPU na krok — {title}")
        ax.legend(loc="upper left")
    axes[0].set_ylabel("zużycie CPU [rdzenie]")
    fig.tight_layout()
    save(
        fig,
        out,
        "capacity-cpu-per-step",
        "campaign …/03-round/capacity-per-step-cpu.json (Prometheus, okno kroku)",
        "Zużycie CPU [rdzenie] bazy danych (lewy panel) i obu replik aplikacji (prawy panel) na kolejnych krokach "
        "profilu capacity; linie przerywane = limity kontenerów (baza 1 CPU, aplikacja 2 × 1 CPU). Kolano wyznacza baza.",
    )


def fig_cpu_by_layer(summary: dict, out: str) -> None:
    a, b = summary.get("demo/a"), summary.get("demo/b")
    if not a or not b:
        return
    na, nb = a.get("runs", 5), b.get("runs", 5)
    n_str = f"N = {na}" if na == nb else f"N = {na}/{nb}"
    keys = [
        ("app_cpu_ms_per_journey", "aplikacja"),
        ("db_cpu_ms_per_journey", "baza danych"),
        ("bus_cpu_ms_per_journey", "szyna (RabbitMQ / Redis)"),
        ("ingress_cpu_ms_per_journey", "ingress (gzip, TLS)"),
        ("ns_cpu_ms_per_journey", "cała przestrzeń nazw"),
        ("ns_marginal_cpu_ms_per_journey", "przestrzeń nazw — marginalnie"),
    ]
    keys = [k for k in keys if k[0] in a["resources"] and k[0] in b["resources"]]
    fig, ax = plt.subplots(figsize=(8.4, 4.4))
    x = range(len(keys))
    w = 0.38
    ya = [m(a["resources"][k]) for k, _ in keys]
    yb = [m(b["resources"][k]) for k, _ in keys]
    ea = [m(a["resources"][k], "ci95", 0) or 0 for k, _ in keys]
    eb = [m(b["resources"][k], "ci95", 0) or 0 for k, _ in keys]
    ax.bar([i - w / 2 for i in x], ya, w, yerr=ea, color=A_COLOR, capsize=2, label="A — SPA + .NET")
    ax.bar([i + w / 2 for i in x], yb, w, yerr=eb, color=B_COLOR, capsize=2, label="B — Go SSR")
    ax.set_xticks(list(x))
    ax.set_xticklabels([lbl for _, lbl in keys], rotation=25, ha="right")
    ax.set_ylabel("czas CPU na podróż [ms]")
    ax.set_title(f"CPU na obsłużoną podróż według warstwy (demo, {n_str})")
    ax.legend()
    fig.tight_layout()
    save(
        fig,
        out,
        "cpu-per-journey-by-layer-demo",
        "round-summary.json → summary.demo/{a,b}.resources.*_cpu_ms_per_journey",
        f"Czas CPU zużyty na jedną obsłużoną podróż [ms], osobno dla warstwy aplikacji, bazy danych, szyny komunikatów, "
        f"współdzielonego ingressu oraz łącznie dla całej przestrzeni nazw (całkowity i marginalny, po odjęciu kosztu spoczynku); "
        f"profil demo, średnia z {n_str} powtórzeń, słupki błędu = 95 % CI.",
    )

    mkeys = [
        ("app_mem_avg_mib", "aplikacja (2 repliki)"),
        ("db_mem_avg_mib", "baza danych"),
        ("bus_mem_avg_mib", "szyna"),
        ("frontend_mem_avg_mib", "pod frontu (tylko A)"),
        ("ns_mem_avg_mib", "cała przestrzeń nazw"),
    ]
    mkeys = [k for k in mkeys if k[0] in a["resources"]]
    fig, ax = plt.subplots(figsize=(8.0, 4.2))
    x = range(len(mkeys))
    ya = [m(a["resources"][k]) or 0 for k, _ in mkeys]
    yb = [m(b["resources"].get(k)) or 0 for k, _ in mkeys]
    ax.bar([i - w / 2 for i in x], ya, w, color=A_COLOR, label="A — SPA + .NET")
    ax.bar([i + w / 2 for i in x], yb, w, color=B_COLOR, label="B — Go SSR")
    ax.set_xticks(list(x))
    ax.set_xticklabels([lbl for _, lbl in mkeys], rotation=20, ha="right")
    ax.set_ylabel("średnie zużycie pamięci (working set) [MiB]")
    ax.set_title(f"Pamięć według warstwy (demo, {n_str})")
    ax.legend()
    fig.tight_layout()
    save(
        fig,
        out,
        "memory-by-layer-demo",
        "round-summary.json → summary.demo/{a,b}.resources.*_mem_avg_mib",
        "Średnie zużycie pamięci [MiB] w oknie scenariusza demo według warstwy; A pracuje z domyślnym Server GC .NET, "
        "B z domyślnym GOGC=100 (odchylenie D8).",
    )


def fig_repeats(summary: dict, stats: dict | None, out: str) -> None:
    a, b = summary.get("demo/a"), summary.get("demo/b")
    if not a or not b:
        return
    # h6 archives: the core p95 (without J4) is the headline latency and carries its own test row; older archives
    # have only the all-journey p95 (the stats label changed with h6 — both spellings are looked up).
    if a["totals"].get("journey_core_p95_ms") and b["totals"].get("journey_core_p95_ms"):
        p95_row = ("p95 podróży bez J4 [ms]", a["totals"]["journey_core_p95_ms"], b["totals"]["journey_core_p95_ms"],
                   "journey latency p95 (ms, core = without J4)")
    else:
        p95_row = ("p95 podróży [ms]", a["totals"]["journey_p95_ms"], b["totals"]["journey_p95_ms"],
                   "journey latency p95 (ms, all journeys)")
    rows = [
        p95_row,
        ("CPU bazy / podróż [ms]", a["resources"]["db_cpu_ms_per_journey"], b["resources"]["db_cpu_ms_per_journey"], "DB CPU per journey (ms)"),
        ("CPU aplikacji / podróż [ms]", a["resources"]["app_cpu_ms_per_journey"], b["resources"]["app_cpu_ms_per_journey"], "app CPU per journey (ms)"),
        ("CPU przestrz. nazw marg. / podróż [ms]", a["resources"]["ns_marginal_cpu_ms_per_journey"], b["resources"]["ns_marginal_cpu_ms_per_journey"], "WHOLE namespace MARGINAL CPU per journey (ms)"),
        ("pamięć aplikacji [MiB]", a["resources"]["app_mem_avg_mib"], b["resources"]["app_mem_avg_mib"], "app memory avg (MiB)"),
    ]
    pvals = {}
    if stats and "demo" in stats:
        for r in stats["demo"]["headline"]:
            pvals[r["metric"]] = r
    fig, axes = plt.subplots(len(rows), 1, figsize=(8.4, 1.5 * len(rows) + 0.6))
    for ax, (label, da, db, metric) in zip(axes, rows):
        va, vb = da["values"], db["values"]
        ax.scatter(va, [1] * len(va), color=A_COLOR, s=46, zorder=3, label="A")
        ax.scatter(vb, [0] * len(vb), color=B_COLOR, s=46, marker="s", zorder=3, label="B")
        ax.vlines(da["mean"], 0.75, 1.25, color=A_COLOR, lw=2)
        ax.vlines(db["mean"], -0.25, 0.25, color=B_COLOR, lw=2)
        ax.set_yticks([0, 1])
        ax.set_yticklabels(["B", "A"])
        ax.set_ylim(-0.6, 1.6)
        ttl = label
        if metric in pvals:
            p = pvals[metric]
            fp = lambda v: "< 0.001" if v < 0.001 else f"= {v:.3f}"
            ttl += f"   —   Welch p {fp(p['welch_p'])}, Mann-Whitney p {fp(p['mw_p'])}, δ Cliffa = {p['cliffs_delta']:+.2f}"
        ax.set_title(ttl, loc="left", fontsize=9.5)
        ax.grid(axis="y", visible=False)
    na = len(rows[0][1]["values"]) if rows else a.get("runs", 5)
    nb = len(rows[0][2]["values"]) if rows else b.get("runs", 5)
    n_str = f"N = {na}" if na == nb else f"N = {na}/{nb}"
    fig.suptitle(
        f"Każde powtórzenie osobno (demo, {n_str} na stronę): A = niebieskie kółka, B = pomarańczowe kwadraty; kreska = średnia",
        fontsize=10.5, x=0.02, ha="left",
    )
    fig.tight_layout()
    save(
        fig,
        out,
        "per-repeat-values-demo",
        "round-summary.json → …values[]; stats.json → demo.headline (Welch, Mann-Whitney, Cliff)",
        "Wartości z pojedynczych powtórzeń profilu demo (kropki) i średnie (kreski) dla pięciu metryk; w tytule każdego "
        "panelu wartości p testu Welcha i dokładnego testu Manna-Whitneya oraz delta Cliffa. Pokazuje, gdzie strony się rozdzielają "
        "(p95, CPU bazy, pamięć), a gdzie przeplatają (CPU aplikacji).",
    )


def parse_openmetrics(path: str) -> dict:
    out: dict = {}
    rx = re.compile(r'^(\w+)\{([^}]*)\}\s+([0-9.eE+-]+)')
    for line in open(path):
        mt = rx.match(line.strip())
        if not mt:
            continue
        name, labels, val = mt.groups()
        lab = dict(kv.split("=", 1) for kv in labels.split(","))
        lab = {k: v.strip('"') for k, v in lab.items()}
        page = "feed" if "/c/" in lab["path"] else "thread"
        key = lab.get("category") or lab.get("metric")
        out[(page, name, key)] = float(val)
    return out


def fig_lighthouse(lh_dir: str, out: str) -> None:
    if not lh_dir or not os.path.isdir(lh_dir):
        return
    data = {}
    for side in ("a", "b"):
        for preset in ("mobile", "desktop"):
            p = os.path.join(lh_dir, f"lighthouse-metrics-cluster-{side}-{preset}.txt")
            if os.path.exists(p):
                data[(side, preset)] = parse_openmetrics(p)
    if len(data) < 4:
        return
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2))
    cats = [("feed", "mobile"), ("thread", "mobile"), ("feed", "desktop"), ("thread", "desktop")]
    labels = ["feed\nmobile", "wątek\nmobile", "feed\ndesktop", "wątek\ndesktop"]
    x = range(len(cats))
    w = 0.38
    for ax, (metric_name, key, ylabel, title) in zip(
        axes,
        (
            ("lighthouse_category_score", "performance", "wynik Performance [0–1]", "Lighthouse: wynik Performance"),
            ("lighthouse_web_vital", "tbt_ms", "Total Blocking Time [ms]", "Lighthouse: Total Blocking Time"),
        ),
    ):
        ya = [data[("a", pr)].get((pg, metric_name, key), 0) for pg, pr in cats]
        yb = [data[("b", pr)].get((pg, metric_name, key), 0) for pg, pr in cats]
        ax.bar([i - w / 2 for i in x], ya, w, color=A_COLOR, label="A — SPA + .NET")
        ax.bar([i + w / 2 for i in x], yb, w, color=B_COLOR, label="B — Go SSR")
        ax.set_xticks(list(x))
        ax.set_xticklabels(labels)
        ax.set_ylabel(ylabel)
        ax.set_title(title)
        if key == "performance":
            ax.set_ylim(0, 1.05)
    axes[0].legend(loc="lower right")
    fig.tight_layout()
    save(
        fig,
        out,
        "lighthouse-cluster",
        f"{os.path.basename(lh_dir)}/lighthouse-metrics-cluster-*.txt (mediana z 3 przebiegów)",
        "Lighthouse na wdrożeniu benchmarkowym przez ingress: wynik kategorii Performance [0–1] (lewy panel) i Total Blocking "
        "Time [ms] (prawy panel) dla strony feedu i strony wątku, emulacja telefonu (mobile: slow-4G, 4× spowolnienie CPU) "
        "oraz desktop; mediana z trzech przebiegów. Przebieg walidacyjny (do powtórzenia na nieobciążonej maszynie).",
    )


def fig_chaos(chaos_dir: str, out: str) -> None:
    if not chaos_dir or not os.path.isdir(chaos_dir):
        return
    rows = {}
    for rep in glob.glob(os.path.join(chaos_dir, "*", "CHAOS-REPORT.md")):
        side = "A" if "-A-" in rep else "B" if "-B-" in rep else None
        if not side:
            continue
        for line in open(rep):
            if line.startswith("| pod-crash") or line.startswith("| db-restart"):
                cells = [c.strip() for c in line.strip().strip("|").split("|")]
                fault, failed, recovery, maxp95 = cells[0], cells[4], cells[5], cells[6]
                rows[(side, fault)] = (float(failed), float(recovery), float(maxp95))
    if len(rows) < 4:
        return
    faults = ["pod-crash", "db-restart"]
    fl = ["zabicie poda aplikacji", "restart Postgresa"]
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.9))
    w = 0.38
    for ax, idx, ylabel, title in zip(
        axes,
        (0, 1, 2),
        ("nieudane żądania [liczba]", "czas odzyskania [s]", "maks. p95 po awarii [ms]"),
        ("Nieudane żądania", "Czas odzyskania", "Najgorszy p95 po awarii"),
    ):
        x = range(len(faults))
        ya = [rows[("A", f)][idx] for f in faults]
        yb = [rows[("B", f)][idx] for f in faults]
        ax.bar([i - w / 2 for i in x], ya, w, color=A_COLOR, label="A — SPA + .NET")
        ax.bar([i + w / 2 for i in x], yb, w, color=B_COLOR, label="B — Go SSR")
        ax.set_xticks(list(x))
        ax.set_xticklabels(fl)
        ax.set_ylabel(ylabel)
        ax.set_title(title)
    axes[0].legend(loc="upper left")
    fig.tight_layout()
    save(
        fig,
        out,
        "chaos-faults",
        f"{os.path.basename(chaos_dir)}/*/CHAOS-REPORT.md (ab_chaos_report.py, kubełki 5 s)",
        "Test odporności pod obciążeniem demo: liczba nieudanych żądań, czas odzyskania [s] i najwyższy p95 po awarii [ms] "
        "dla zabicia jednego poda aplikacji oraz restartu bazy danych; po jednym przebiegu na stronę (przebiegi walidacyjne).",
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--round", required=True)
    ap.add_argument("--capacity-cpu", default="")
    ap.add_argument("--lighthouse", default="")
    ap.add_argument("--chaos", default="")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    rs = json.load(open(os.path.join(args.round, "round-summary.json")))
    summary = rs["summary"]
    stats_path = os.path.join(args.round, "stats.json")
    stats = json.load(open(stats_path)) if os.path.exists(stats_path) else None
    os.makedirs(args.out, exist_ok=True)
    print(f"round {rs.get('round')} → {args.out}")

    n_demo = summary.get("demo/a", {}).get("runs", 0)
    fig_latency_per_journey(summary, "demo", args.out, f"N = {n_demo}")
    if "stress/a" in summary:
        fig_latency_per_journey(summary, "stress", args.out, f"N = {summary['stress/a']['runs']}")
    fig_capacity(summary, args.out)
    fig_capacity_cpu(args.capacity_cpu, args.out)
    fig_cpu_by_layer(summary, args.out)
    fig_repeats(summary, stats, args.out)
    fig_lighthouse(args.lighthouse, args.out)
    fig_chaos(args.chaos, args.out)

    with open(os.path.join(args.out, "README.md"), "w") as f:
        f.write(f"# Thesis figures — round `{rs.get('round')}`\n\n")
        f.write("Generated by `scripts/thesis_figures.py` (never edit the images by hand; re-run the script).\n")
        f.write("Every figure exists as PNG (200 dpi) and SVG. Captions are in Polish, units are on every axis.\n\n")
        f.write("| file | data source | suggested caption (PL) |\n|---|---|---|\n")
        for name, src, cap in README:
            f.write(f"| `{name}.png` / `.svg` | {src} | {cap} |\n")
    print(f"README.md with {len(README)} figures")
    return 0


if __name__ == "__main__":
    sys.exit(main())
