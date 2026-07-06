"use client";

/** Root error boundary — the design's "// SIGNAL LOST" generic template. */

import { GenericErrorState } from "@/components/ui/ErrorState";

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <div style={{ maxWidth: 700, margin: "80px auto", padding: "0 24px" }}>
      <GenericErrorState onRetry={reset} detail={error.digest} />
    </div>
  );
}
