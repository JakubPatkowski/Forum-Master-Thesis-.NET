"use client";

/**
 * Feed/search row (design: Home.dc.html rows). Notes against the contract:
 *  - no excerpt: ThreadFeedItemResponse carries no body text — the card renders without
 *    one rather than inventing content;
 *  - no comment-count badge: the field is hard-coded 0 server-side (scope decision);
 *  - like count comes from the Engagement batch hydration, not the feed's zeroed field.
 */

import Link from "next/link";

import { ReactionButton } from "@/components/engagement/ReactionButton";
import { Badge } from "@/components/ui/Badge";
import { Monogram } from "@/components/ui/Monogram";
import type { ReactionSummaryResponse, ThreadFeedItemResponse } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./ThreadCard.module.css";

export interface ThreadCardProps {
  thread: ThreadFeedItemResponse;
  reaction?: ReactionSummaryResponse;
  /** Show the category label (hidden on category pages where it's redundant). */
  showCategory?: boolean;
  /** Moderator affordance (heuristic; the real gate is the server's 403). */
  pinAction?: { pinned: boolean; onToggle: () => void };
  /** Cyan glow for realtime-arrived rows. */
  isNew?: boolean;
}

export function ThreadCard({
  thread,
  reaction,
  showCategory = true,
  pinAction,
  isNew = false,
}: ThreadCardProps) {
  return (
    <div className={isNew ? `${styles.card} ${styles.cardNew}` : styles.card}>
      <Link href={`/t/${thread.id}`} className={styles.main}>
        <Monogram name={thread.categoryName} seed={thread.categorySlug} size={44} />
        <span className={styles.body}>
          <span className={styles.meta}>
            {thread.isPinned ? (
              <Badge tone="accent">
                <PinIcon />
                PINNED
              </Badge>
            ) : null}
            {showCategory ? (
              <span className={styles.category}>{thread.categoryName.toUpperCase()}</span>
            ) : null}
            <span className={styles.time}>{timeAgoLabel(thread.createdOnUtc)}</span>
            {thread.lastModifiedOnUtc ? <Badge>EDITED</Badge> : null}
          </span>
          <span className={styles.title}>{thread.title}</span>
          <span className={styles.author}>@{thread.username}</span>
        </span>
      </Link>
      <span className={styles.side}>
        {pinAction ? (
          <button
            className={styles.pinButton}
            onClick={pinAction.onToggle}
            title={pinAction.pinned ? "Unpin thread" : "Pin thread"}
          >
            {pinAction.pinned ? "UNPIN" : "PIN"}
          </button>
        ) : null}
        <ReactionButton targetType="thread" targetId={thread.id} initial={reaction} size="sm" />
      </span>
    </div>
  );
}

function PinIcon() {
  return (
    <svg width="9" height="9" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
      <path d="M14 3h-4v2h1v5l-3 3v2h4v6l1 1 1-1v-6h4v-2l-3-3V5h1V3z" />
    </svg>
  );
}
