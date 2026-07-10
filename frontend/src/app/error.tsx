"use client";

/**
 * Route-segment error boundary — the design's "// SIGNAL LOST" generic template.
 * Errors thrown by the root layout itself land in global-error.tsx instead.
 */

import { GenericErrorState } from "@/components/ui/ErrorState";

export default function RouteError({
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
