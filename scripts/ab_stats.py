#!/usr/bin/env python3
"""Hypothesis tests for an A/B round (thesis harness v1/v2) — pure Python, no SciPy on the benchmark host.

For every metric that `ab_report.py` aggregates per (profile, target) this module compares the per-repeat values
of A and B (the repeat is the experimental unit: every repeat starts from a reseeded dataset and fresh pods):

  * Welch's t-test (unequal variances), two-sided p from the Student-t CDF (regularized incomplete beta);
  * exact two-sided Mann-Whitney U (the null distribution of U is enumerated — with 5 vs 5 the smallest
    reachable p is 2/252 = 0.0079, which is why that value appears so often in small rounds);
  * Cliff's delta (effect size in [-1, 1]; |δ| ≥ 0.474 is conventionally "large");
  * Holm-Bonferroni adjustment within a family of metrics (the headline table is one family, the per-journey
    table another), so a thesis table never reports 20 raw p-values as if they were one. h6: the PRIMARY test is
    the exact Mann-Whitney U (non-parametric — the methodology's choice for latency/resource distributions);
    Welch's t and its own Holm-adjusted p are reported as the parametric cross-check.

A verdict is attached per metric: `distinct` (MW Holm-adjusted p < 0.05), `raw-only` (raw MW p < 0.05, not after
the correction — report as suggestive), `underpowered` (even complete separation could not pass Holm at this n —
e.g. 5 vs 5 over 14 tests: 0.0079 × 14 > 0.05; N ≥ 10 per side resolves it), `no difference detected` (raw p ≥ 0.05
— NOT "equal": no equivalence test is run) or `n<3` (too few repeats for any verdict).

CLI:  ab_stats.py <round-dir>          # prints the tables; also importable (run_round_tests)
"""
import json
import math
import os
import sys
from itertools import combinations

ALPHA = 0.05

# ---------------------------------------------------------------- special functions (Numerical Recipes style)


def _ln_gamma(x):
    return math.lgamma(x)


def _betacf(a, b, x, max_iter=300, eps=3e-14):
    """Continued fraction for the incomplete beta function (Lentz's method)."""
    tiny = 1e-300
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    d = tiny if abs(d) < tiny else d
    d = 1.0 / d
    h = d
    for m in range(1, max_iter + 1):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < eps:
            break
    return h


def betainc(a, b, x):
    """Regularized incomplete beta I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    ln_bt = _ln_gamma(a + b) - _ln_gamma(a) - _ln_gamma(b) + a * math.log(x) + b * math.log(1.0 - x)
    bt = math.exp(ln_bt)
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def t_two_sided_p(t, df):
    """Two-sided p-value of a Student-t statistic with df degrees of freedom."""
    if df <= 0 or math.isnan(t):
        return float("nan")
    x = df / (df + t * t)
    return betainc(df / 2.0, 0.5, x)


# ---------------------------------------------------------------- tests


def _clean(values):
    return [float(v) for v in (values or []) if v is not None and not (isinstance(v, float) and math.isnan(v))]


def welch(a, b):
    """Welch's t-test → dict(t, df, p) or None when either side has < 2 values."""
    a, b = _clean(a), _clean(b)
    if len(a) < 2 or len(b) < 2:
        return None
    ma, mb = sum(a) / len(a), sum(b) / len(b)
    va = sum((x - ma) ** 2 for x in a) / (len(a) - 1)
    vb = sum((x - mb) ** 2 for x in b) / (len(b) - 1)
    se2 = va / len(a) + vb / len(b)
    if se2 == 0:
        return {"t": 0.0 if ma == mb else math.copysign(float("inf"), mb - ma), "df": len(a) + len(b) - 2,
                "p": 1.0 if ma == mb else 0.0}
    t = (mb - ma) / math.sqrt(se2)
    num = se2 ** 2
    den = (va / len(a)) ** 2 / (len(a) - 1) + (vb / len(b)) ** 2 / (len(b) - 1)
    df = num / den if den > 0 else len(a) + len(b) - 2
    return {"t": t, "df": df, "p": t_two_sided_p(t, df)}


def _u_null_distribution(m, n):
    """Frequencies of U = 0..m*n under H0 (no ties), by dynamic programming over the m+n ranks."""
    # f[i][j] = list of counts indexed by u for i values of A and j of B
    f = [[None] * (n + 1) for _ in range(m + 1)]
    for i in range(m + 1):
        for j in range(n + 1):
            if i == 0 or j == 0:
                f[i][j] = [1]
                continue
            prev_a = f[i - 1][j]  # largest rank belongs to A: contributes j to U (A beats all j B's)
            prev_b = f[i][j - 1]  # largest rank belongs to B: contributes 0
            size = i * j + 1
            row = [0] * size
            for u, c in enumerate(prev_a):
                row[u + j] += c
            for u, c in enumerate(prev_b):
                row[u] += c
            f[i][j] = row
    return f[m][n]


def mann_whitney_exact(a, b):
    """Exact two-sided Mann-Whitney U (ties get half credit in U; the null distribution assumes no ties)."""
    a, b = _clean(a), _clean(b)
    m, n = len(a), len(b)
    if m == 0 or n == 0:
        return None
    u_a = sum(1.0 if x > y else 0.5 if x == y else 0.0 for x in a for y in b)
    u_b = m * n - u_a
    u = min(u_a, u_b)
    freq = _u_null_distribution(m, n)
    total = sum(freq)
    # Two-sided: twice the lower tail of min(U_A, U_B) (the distribution is symmetric around m*n/2).
    lower = sum(freq[k] for k in range(0, int(math.floor(u)) + 1))
    p = min(1.0, 2.0 * lower / total)
    return {"u": u, "u_a": u_a, "u_b": u_b, "p": p, "min_p": min(1.0, 2.0 * freq[0] / total)}


def cliffs_delta(a, b):
    """P(B > A) − P(B < A); positive = B tends to be larger."""
    a, b = _clean(a), _clean(b)
    if not a or not b:
        return None
    more = sum(1 for x in a for y in b if y > x)
    less = sum(1 for x in a for y in b if y < x)
    return (more - less) / (len(a) * len(b))


def holm(pvalues):
    """Holm-Bonferroni adjusted p-values (same order as the input; None stays None)."""
    idx = [i for i, p in enumerate(pvalues) if p is not None and not math.isnan(p)]
    m = len(idx)
    adjusted = [None] * len(pvalues)
    if m == 0:
        return adjusted
    order = sorted(idx, key=lambda i: pvalues[i])
    running = 0.0
    for rank, i in enumerate(order):
        value = min(1.0, (m - rank) * pvalues[i])
        running = max(running, value)
        adjusted[i] = running
    return adjusted


def verdict(n_a, n_b, p_raw, p_adj, min_p=None, family_size=1):
    """Primary verdict from the exact Mann-Whitney p (non-parametric, methodology §10) after Holm over the family.
    `underpowered` = even complete separation of the two samples (min_p = 2/C(n_a+n_b, n_a)) could not pass the
    Holm-adjusted threshold at this n — e.g. 5 vs 5 over 14 tests (0.0079 × 14 > 0.05); N ≥ 10 per side resolves it."""
    if n_a < 3 or n_b < 3:
        return "n<3"
    if p_adj is not None and p_adj < ALPHA:
        return "distinct"
    unreachable = min_p is not None and min_p * family_size > ALPHA
    if p_raw is not None and p_raw < ALPHA:
        return "raw-only (underpowered for Holm)" if unreachable else "raw-only"
    if unreachable:
        return "underpowered"
    return "no difference detected"


# ---------------------------------------------------------------- round-level driver

HEADLINE = [
    ("journey latency median (ms, core = without J4)", "totals", "journey_core_med_ms"),
    ("journey latency p95 (ms, core = without J4)", "totals", "journey_core_p95_ms"),
    ("journey latency median (ms, all journeys)", "totals", "journey_med_ms"),
    ("journey latency p95 (ms, all journeys)", "totals", "journey_p95_ms"),
    ("HTTP request p95 (ms)", "totals", "http_req_p95_ms"),
    ("KiB received per journey", "totals", "kib_received_per_journey"),
    ("app CPU per journey (ms)", "resources", "app_cpu_ms_per_journey"),
    ("app MARGINAL CPU per journey (ms)", "resources", "app_marginal_cpu_ms_per_journey"),
    ("app memory avg (MiB)", "resources", "app_mem_avg_mib"),
    ("DB CPU per journey (ms)", "resources", "db_cpu_ms_per_journey"),
    ("bus CPU per journey (ms)", "resources", "bus_cpu_ms_per_journey"),
    ("WHOLE namespace CPU per journey (ms)", "resources", "ns_cpu_ms_per_journey"),
    ("WHOLE namespace MARGINAL CPU per journey (ms)", "resources", "ns_marginal_cpu_ms_per_journey"),
    ("WHOLE namespace idle cores", "resources", "ns_idle_avg_cores"),
    ("WHOLE namespace memory avg (MiB)", "resources", "ns_mem_avg_mib"),
    ("ingress-nginx CPU per journey (ms)", "resources", "ingress_cpu_ms_per_journey"),
    ("WS receipt latency p95 (ms)", "ws", "receipt_p95_ms"),
    ("WS probe loss (%)", "ws", "probe_loss_pct"),
]
JOURNEY_ORDER = ["J1", "J2", "J3", "J4", "J5", "J6", "J7", "J9", "nav"]


def _values(entry, section, key):
    s = (entry or {}).get(section, {}).get(key)
    return s.get("values") if s else None


def compare(label, va, vb):
    va, vb = _clean(va), _clean(vb)
    w = welch(va, vb)
    mw = mann_whitney_exact(va, vb)
    d = cliffs_delta(va, vb)
    ma = sum(va) / len(va) if va else None
    mb = sum(vb) / len(vb) if vb else None
    return {
        "metric": label, "n_a": len(va), "n_b": len(vb), "mean_a": ma, "mean_b": mb,
        "diff_pct": ((mb - ma) / ma * 100.0) if (ma not in (None, 0) and mb is not None) else None,
        "welch_t": w["t"] if w else None, "welch_df": w["df"] if w else None, "welch_p": w["p"] if w else None,
        "mw_u": mw["u"] if mw else None, "mw_p": mw["p"] if mw else None, "mw_min_p": mw["min_p"] if mw else None,
        "cliffs_delta": d,
    }


def run_family(rows):
    """Holm over the exact Mann-Whitney p-values (primary — non-parametric, methodology §10); Welch's t is kept as
    the parametric cross-check (its own Holm-adjusted p in `welch_holm_p`). h6: before, the verdict read Welch."""
    adj_mw = holm([r["mw_p"] for r in rows])
    adj_w = holm([r["welch_p"] for r in rows])
    for r, p_mw, p_w in zip(rows, adj_mw, adj_w):
        r["holm_p"] = p_mw
        r["welch_holm_p"] = p_w
        r["family_size"] = len(rows)
        r["verdict"] = verdict(r["n_a"], r["n_b"], r["mw_p"], p_mw, r.get("mw_min_p"), len(rows))
    return rows


def run_round_tests(summary):
    """summary = ab_report.aggregate(...) output → {profile: {"headline": [...], "journeys": [...]}}."""
    out = {}
    profiles = sorted({k.split("/")[0] for k in summary})
    for p in profiles:
        a, b = summary.get(f"{p}/a"), summary.get(f"{p}/b")
        if not a or not b:
            continue
        headline = []
        for label, section, key in HEADLINE:
            va, vb = _values(a, section, key), _values(b, section, key)
            if va and vb:
                headline.append(compare(label, va, vb))
        journeys = []
        for j in JOURNEY_ORDER:
            ja, jb = (a.get("journeys") or {}).get(j), (b.get("journeys") or {}).get(j)
            if not ja or not jb:
                continue
            for q, name in (("med_ms", "median"), ("p95_ms", "p95")):
                va, vb = (ja.get(q) or {}).get("values"), (jb.get(q) or {}).get("values")
                if va and vb:
                    journeys.append(compare(f"{j} {name} (ms)", va, vb))
        out[p] = {"headline": run_family(headline), "journeys": run_family(journeys)}
    return out


def _fmt_p(p):
    if p is None or (isinstance(p, float) and math.isnan(p)):
        return "–"
    return "<0.001" if p < 0.001 else f"{p:.3f}"


def _fmt(v, digits=2):
    return "–" if v is None else f"{v:.{digits}f}"


def markdown_tables(tests):
    md = []
    for p, fam in tests.items():
        for name, rows in (("headline metrics", fam["headline"]), ("per-journey latency", fam["journeys"])):
            if not rows:
                continue
            n = f"n = {rows[0]['n_a']} vs {rows[0]['n_b']}"
            md.append(f"#### `{p}` — {name} ({n}; Holm over {len(rows)} tests)")
            md.append("| metric | A mean | B mean | B−A | MW exact p | MW Holm p | Cliff's δ | Welch p | Welch Holm p | verdict |")
            md.append("|---|---|---|---|---|---|---|---|---|---|")
            for r in rows:
                md.append("| " + " | ".join([
                    r["metric"], _fmt(r["mean_a"], 3), _fmt(r["mean_b"], 3),
                    ("–" if r["diff_pct"] is None else f"{r['diff_pct']:+.1f} %"),
                    _fmt_p(r["mw_p"]), _fmt_p(r.get("holm_p")), _fmt(r["cliffs_delta"]),
                    _fmt_p(r["welch_p"]), _fmt_p(r.get("welch_holm_p")),
                    r["verdict"]]) + " |")
            md.append("")
    return "\n".join(md)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    round_dir = os.path.abspath(sys.argv[1])
    summary_path = os.path.join(round_dir, "round-summary.json")
    if not os.path.exists(summary_path):
        sys.exit(f"{summary_path} missing — run ab_report.py first")
    summary = json.load(open(summary_path))["summary"]
    tests = run_round_tests(summary)
    print(markdown_tables(tests))
    json.dump(tests, open(os.path.join(round_dir, "stats.json"), "w"), indent=2)


if __name__ == "__main__":
    main()
