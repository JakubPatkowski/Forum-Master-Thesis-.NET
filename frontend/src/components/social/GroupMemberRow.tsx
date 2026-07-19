"use client";

/**
 * One member of a group: presence avatar, profile link, OWNER/ADMIN badges, and —
 * when the viewer administers the group — an overflow menu with promote/demote, kick
 * and (owner only) transfer-ownership. The owner can never be kicked or demoted
 * (backend enforces it; the UI simply never offers those actions).
 */

import Link from "next/link";
import { useEffect, useRef, useState } from "react";

import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import { Badge } from "@/components/ui/Badge";
import type { GroupMemberResponse, PresenceStatus } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function GroupMemberRow({
  member,
  status,
  isSelf,
  viewerIsAdmin,
  viewerIsOwner,
  onMessage,
  onSetRole,
  onKick,
  onTransferOwnership,
}: {
  member: GroupMemberResponse;
  status?: PresenceStatus;
  isSelf: boolean;
  viewerIsAdmin: boolean;
  viewerIsOwner: boolean;
  onMessage?: () => void;
  onSetRole?: (role: "admin" | "member") => void;
  onKick?: () => void;
  onTransferOwnership?: () => void;
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

  const canManage = viewerIsAdmin && !isSelf && !member.isOwner;

  return (
    <div className={styles.row}>
      <PresenceAvatar userId={member.userId} username={member.username} status={status} />
      <span className={styles.text}>
        <Link href={`/u/${member.userId}`} className={styles.name}>
          {member.username}
          {isSelf ? " (you)" : ""}
        </Link>
        <span className={styles.sub}>joined {timeAgoLabel(member.joinedOnUtc)} ago</span>
      </span>
      <span className={styles.badges}>
        {member.isOwner ? <Badge tone="accent">OWNER</Badge> : null}
        {!member.isOwner && member.isAdmin ? <Badge tone="cyan">ADMIN</Badge> : null}
      </span>
      {onMessage && !isSelf ? (
        <button className={styles.iconButton} title="Message" onClick={onMessage}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
            <path d="M4 4h16a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H8l-5 4V5a1 1 0 0 1 1-1z" />
          </svg>
        </button>
      ) : null}
      {canManage ? (
        <div className={styles.menuAnchor} ref={menuRef}>
          <button className={styles.iconButton} title="Manage" onClick={() => setMenuOpen((v) => !v)}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
              <path d="M6 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm8 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0zm8 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0z" />
            </svg>
          </button>
          {menuOpen ? (
            <div className={styles.menu}>
              {onSetRole ? (
                <button
                  className={styles.menuItem}
                  onClick={() => {
                    setMenuOpen(false);
                    onSetRole(member.isAdmin ? "member" : "admin");
                  }}
                >
                  {member.isAdmin ? "DEMOTE TO MEMBER" : "PROMOTE TO ADMIN"}
                </button>
              ) : null}
              {viewerIsOwner && onTransferOwnership ? (
                <button
                  className={styles.menuItem}
                  onClick={() => {
                    setMenuOpen(false);
                    onTransferOwnership();
                  }}
                >
                  TRANSFER OWNERSHIP
                </button>
              ) : null}
              {onKick ? (
                <button
                  className={`${styles.menuItem} ${styles.menuDanger}`}
                  onClick={() => {
                    setMenuOpen(false);
                    onKick();
                  }}
                >
                  KICK FROM GROUP
                </button>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
