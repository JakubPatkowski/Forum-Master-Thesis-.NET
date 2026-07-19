"use client";

/**
 * One friend in a social list: presence avatar, name → profile link, status/subtitle
 * line, message button and an overflow menu (remove / block). Pure props — reusable
 * for the friends rail, member pickers, or any user list with presence.
 */

import Link from "next/link";
import { useEffect, useRef, useState } from "react";

import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import type { PresenceStatus } from "@/lib/api/types";
import { presenceLabel } from "@/lib/social/presence";

import styles from "./rows.module.css";

export function FriendRow({
  userId,
  username,
  status,
  subtitle,
  onMessage,
  onRemove,
  onBlock,
}: {
  userId: string;
  username: string;
  status?: PresenceStatus;
  /** Overrides the presence label line (e.g. "friends since …"). */
  subtitle?: string;
  onMessage?: () => void;
  onRemove?: () => void;
  onBlock?: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const handler = (event: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [menuOpen]);

  const statusLine = subtitle ?? (status ? presenceLabel(status) : "");
  const subClass =
    status === "online"
      ? `${styles.sub} ${styles.subOnline}`
      : status === "away"
        ? `${styles.sub} ${styles.subAway}`
        : styles.sub;

  return (
    <div className={status === "offline" ? `${styles.row} ${styles.rowMuted}` : styles.row}>
      <PresenceAvatar userId={userId} username={username} status={status} />
      <span className={styles.text}>
        <Link href={`/u/${userId}`} className={styles.name}>
          {username}
        </Link>
        {statusLine ? <span className={subClass}>{statusLine}</span> : null}
      </span>
      {onMessage ? (
        <button className={styles.iconButton} title="Message" onClick={onMessage}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
            <path d="M4 4h16a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H8l-5 4V5a1 1 0 0 1 1-1z" />
          </svg>
        </button>
      ) : null}
      {onRemove || onBlock ? (
        <div className={styles.menuAnchor} ref={menuRef}>
          <button
            className={styles.iconButton}
            title="More"
            onClick={() => setMenuOpen((v) => !v)}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
              <path d="M6 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm8 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm8 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0z" />
            </svg>
          </button>
          {menuOpen ? (
            <div className={styles.menu}>
              {onRemove ? (
                <button
                  className={`${styles.menuItem} ${styles.menuDanger}`}
                  onClick={() => {
                    setMenuOpen(false);
                    onRemove();
                  }}
                >
                  REMOVE FRIEND
                </button>
              ) : null}
              {onBlock ? (
                <button
                  className={`${styles.menuItem} ${styles.menuDanger}`}
                  onClick={() => {
                    setMenuOpen(false);
                    onBlock();
                  }}
                >
                  BLOCK
                </button>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
