"use client";

/**
 * Error-state components mapped to the design's Errors screen: 404 full-panel, 403
 * full-panel, generic boundary, and the inline banner form for field-less 422s. Any
 * ApiError can be routed through <ApiErrorState> which picks the right template.
 */

import Link from "next/link";
import type { ReactNode } from "react";

import { Button } from "@/components/ui/Button";
import { CornerBrackets } from "@/components/ui/CornerBrackets";
import type { ApiError } from "@/lib/api/problem";

import styles from "./ErrorState.module.css";

export function NotFoundState({
  title = "Not found",
  description = "It may have been deleted — soft-deleted threads return 404 with no placeholder.",
  detail,
}: {
  title?: string;
  description?: string;
  detail?: string;
}) {
  return (
    <div className={styles.panel}>
      <CornerBrackets />
      <div className={styles.code404}>404</div>
      <div className={styles.title}>{title}</div>
      <div className={styles.description}>{description}</div>
      {detail ? <div className={styles.detail}>{detail}</div> : null}
      <div className={styles.actions}>
        <Link href="/">
          <Button variant="secondary">← Back to forum</Button>
        </Link>
      </div>
    </div>
  );
}

export function ForbiddenState({ detail }: { detail?: string }) {
  return (
    <div className={styles.panel}>
      <span className={styles.lockIcon}>
        <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
          <path d="M12 2a5 5 0 0 0-5 5v3H5v12h14V10h-2V7a5 5 0 0 0-5-5zm-3 8V7a3 3 0 1 1 6 0v3H9z" />
        </svg>
      </span>
      <div className={styles.title}>You can&apos;t do that here</div>
      <div className={styles.description}>
        Permission is resolved server-side per action — seeing a button never guarantees the action.
      </div>
      {detail ? <div className={styles.detail}>{detail}</div> : null}
    </div>
  );
}

export function GenericErrorState({ onRetry, detail }: { onRetry?: () => void; detail?: string }) {
  return (
    <div className={styles.panel}>
      <div className={styles.signalLost}>{"// SIGNAL LOST"}</div>
      <div className={styles.title}>Something broke on our side</div>
      <div className={styles.description}>
        Reload the page, or head back to the forum. If it keeps happening, it&apos;s us — not you.
      </div>
      {detail ? <div className={styles.detail}>{detail}</div> : null}
      <div className={styles.actions}>
        {onRetry ? (
          <Button variant="secondary" onClick={onRetry}>
            Retry
          </Button>
        ) : null}
        <Link href="/">
          <Button variant="ghost">Back to forum</Button>
        </Link>
      </div>
    </div>
  );
}

/** Inline banner for field-less validation/API errors (the "422 banner form"). */
export function InlineErrorBanner({ error, extra }: { error: ApiError; extra?: ReactNode }) {
  return (
    <div className={styles.banner} role="alert">
      <span className={styles.bannerStatus}>{error.status || "ERR"}</span>
      <div className={styles.bannerText}>
        <div className={styles.bannerTitle}>{error.title}</div>
        {error.code ? (
          <div className={styles.bannerMeta}>
            code: {error.code} · errorType: {error.errorType}
          </div>
        ) : (
          <div className={styles.bannerMeta}>errorType: {error.errorType}</div>
        )}
      </div>
      {extra}
    </div>
  );
}

/** Routes an ApiError to the matching full-panel template. */
export function ApiErrorState({ error, onRetry }: { error: ApiError; onRetry?: () => void }) {
  const detail = error.code ? `errorType: ${error.errorType} · code: ${error.code}` : undefined;
  if (error.errorType === "NotFound") {
    return <NotFoundState title={error.title} detail={detail} />;
  }
  if (error.errorType === "Forbidden") {
    return <ForbiddenState detail={detail} />;
  }
  return <GenericErrorState onRetry={onRetry} detail={detail ?? error.title} />;
}
