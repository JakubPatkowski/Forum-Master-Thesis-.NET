/**
 * RUM Web Vitals beacon (A-3) — the field-data half of the Q2 instrumentation, mirroring
 * Architecture B's `shared/bridge.ts`: each finalized Core Web Vital (LCP, FCP, INP, TTFB,
 * CLS) is posted as one tiny JSON body to the API's anonymous `/api/telemetry/vitals`
 * endpoint, which folds it into Prometheus histograms (`forum_web_vitals_*`).
 *
 * `navigator.sendBeacon` is used so reports survive tab unload — that is when LCP/INP
 * finalize — degrading to a keepalive fetch where sendBeacon is missing. Reporting is
 * fire-and-forget: a dropped beacon only loses a data point, so there is no retry and no
 * error surface. Dev servers skip reporting entirely (lab noise, mirrors B's skip-vitals
 * escape hatch).
 */
import type { Metric } from "web-vitals";

import { apiUrl } from "@/lib/config";

function send(metric: Metric): void {
  const body = JSON.stringify({
    name: metric.name,
    value: metric.value,
    rating: metric.rating,
  });
  const url = `${apiUrl}/api/telemetry/vitals`;
  if (typeof navigator !== "undefined" && "sendBeacon" in navigator) {
    navigator.sendBeacon(url, body);
  } else {
    void fetch(url, { method: "POST", body, keepalive: true }).catch(() => {
      // fire-and-forget: losing a beacon is fine, surfacing an error is not
    });
  }
}

/** Starts Core Web Vitals reporting. Call once per page load, client-side only. */
export function startWebVitalsReporting(): void {
  if (process.env.NODE_ENV !== "production") return;
  void import("web-vitals")
    .then(({ onCLS, onFCP, onINP, onLCP, onTTFB }) => {
      for (const onVital of [onCLS, onFCP, onINP, onLCP, onTTFB]) {
        onVital(send);
      }
    })
    .catch(() => {
      // loading the reporter is itself best-effort
    });
}
