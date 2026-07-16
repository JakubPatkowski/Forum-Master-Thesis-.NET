"use client";

/**
 * The like toggle — optimistic and instant (backend toggles are idempotent both
 * directions, so there's no confirm dialog and no error UI for the common no-op cases).
 * Anonymous users see the count read-only.
 */

import { useRouter } from "next/navigation";

import { useReactionSummary, useToggleReaction } from "@/lib/hooks/use-reactions";
import type { ReactionSummaryResponse, ReactionTargetType } from "@/lib/api/types";

import styles from "./ReactionButton.module.css";

export function ReactionButton({
  targetType,
  targetId,
  initial,
  covered = false,
  size = "md",
}: {
  targetType: ReactionTargetType;
  targetId: string;
  /** Pre-hydrated summary (e.g. from a batch request) to avoid an extra round-trip. */
  initial?: ReactionSummaryResponse;
  /**
   * True when a page-level useReactionBatch covers this target: the button then renders
   * from the batch's write-through cache and never issues its own single GET.
   */
  covered?: boolean;
  size?: "sm" | "md";
}) {
  const router = useRouter();
  const summary = useReactionSummary(targetType, targetId, initial, covered);
  const { toggle, isAuthenticated } = useToggleReaction(targetType, targetId);

  const count = summary.data?.count ?? 0;
  const reacted = summary.data?.viewerReacted ?? false;

  return (
    <button
      className={[styles.button, styles[size], reacted ? styles.reacted : undefined]
        .filter(Boolean)
        .join(" ")}
      onClick={() => {
        if (!isAuthenticated) {
          router.push("/auth");
          return;
        }
        toggle(reacted);
      }}
      aria-pressed={reacted}
      title={reacted ? "Unlike" : "Like"}
    >
      <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
        <path d="M12 21s-8-5.5-8-11a4.5 4.5 0 0 1 8-2.8A4.5 4.5 0 0 1 20 10c0 5.5-8 11-8 11z" />
      </svg>
      <span>{count}</span>
    </button>
  );
}
