"use client";

/**
 * Root-level error boundary. Unlike error.tsx, this also catches exceptions thrown by
 * the root layout segment itself (provider init included). Next.js replaces the entire
 * root layout with this component, so it must render its own <html>/<body> and import
 * the global stylesheet to keep the design tokens available.
 */

import { GenericErrorState } from "@/components/ui/ErrorState";

import "./globals.css";

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <html lang="en">
      <body>
        <div style={{ maxWidth: 700, margin: "80px auto", padding: "0 24px" }}>
          <GenericErrorState onRetry={reset} detail={error.digest} />
        </div>
      </body>
    </html>
  );
}
