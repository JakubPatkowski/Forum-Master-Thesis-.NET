// Reads Lighthouse CI filesystem output (.bench/lighthouse/manifest.json plus the per-run
// LHR JSON reports) and emits OpenMetrics text — category scores and lab Core Web Vitals
// for each audited page, using the representative (median) run per URL.
//
// Node port of gomx/scripts/lighthouse-metrics.ts with IDENTICAL metric names and labels
// (lighthouse_category_score / lighthouse_web_vital) so the A and B summary files join
// into one thesis table without renaming.
//
// Usage: node scripts/lighthouse-metrics.mjs [report-dir] [output-file]
import { readFile, writeFile, access } from "node:fs/promises";
import path from "node:path";

const dir = process.argv[2] ?? ".bench/lighthouse";
const out = process.argv[3] ?? ".bench/lighthouse-metrics.txt";

// Stable, port-independent label: audit "/t/<id>" not "http://localhost:3000/t/<id>".
function pathOf(u) {
  try {
    return new URL(u).pathname || "/";
  } catch {
    return u;
  }
}

// The manifest may reference reports by absolute path; fall back to the report dir.
async function readJson(filePath) {
  for (const p of [filePath, path.join(dir, path.basename(filePath))]) {
    try {
      await access(p);
      return JSON.parse(await readFile(p, "utf8"));
    } catch {
      /* try next */
    }
  }
  return undefined;
}

const lines = [
  "# HELP lighthouse_category_score Lighthouse category score (0-1, higher is better).",
  "# TYPE lighthouse_category_score gauge",
  "# HELP lighthouse_web_vital Lighthouse lab Core Web Vital (milliseconds; cls is unitless).",
  "# TYPE lighthouse_web_vital gauge",
];

const vitals = [
  ["lcp_ms", "largest-contentful-paint"],
  ["fcp_ms", "first-contentful-paint"],
  ["tbt_ms", "total-blocking-time"],
  ["cls", "cumulative-layout-shift"],
  ["speed_index_ms", "speed-index"],
  ["tti_ms", "interactive"],
];

const manifest = await readJson(path.join(dir, "manifest.json"));
if (!manifest) {
  console.error(`no manifest.json in ${dir} — run lhci collect/upload first`);
  process.exit(1);
}

for (const entry of manifest) {
  if (!entry.isRepresentativeRun) continue;
  const p = pathOf(entry.url);

  for (const [category, score] of Object.entries(entry.summary ?? {})) {
    if (typeof score === "number") {
      lines.push(`lighthouse_category_score{path="${p}",category="${category}"} ${String(score)}`);
    }
  }

  const lhr = await readJson(entry.jsonPath);
  const audits = lhr?.audits ?? {};
  for (const [metric, auditId] of vitals) {
    const value = audits[auditId]?.numericValue;
    if (typeof value === "number") {
      lines.push(`lighthouse_web_vital{path="${p}",metric="${metric}"} ${String(value)}`);
    }
  }
}

await writeFile(out, lines.join("\n") + "\n");
console.log(`wrote ${out} (${lines.length - 4} samples)`);
