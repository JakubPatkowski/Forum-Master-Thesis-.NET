"use client";

/**
 * One group in a directory/rail list: icon, name, member count, visibility, and an
 * OPEN affordance. Visibility affects DISCOVERY/JOIN only — never gate anything else
 * on it (backend invariant).
 */

import { GroupIcon } from "@/components/ui/GroupIcon";
import { Badge } from "@/components/ui/Badge";
import type { GroupSummaryResponse } from "@/lib/api/types";

import styles from "./rows.module.css";

export function GroupCard({
  group,
  selected = false,
  onOpen,
}: {
  group: GroupSummaryResponse;
  selected?: boolean;
  onOpen: () => void;
}) {
  return (
    <button
      className={selected ? `${styles.row} ${styles.rowActive}` : styles.row}
      onClick={onOpen}
    >
      <GroupIcon groupId={group.groupId} name={group.name} size={36} />
      <span className={styles.text}>
        <span className={styles.name}>{group.name}</span>
        <span className={styles.sub}>
          {group.memberCount} {group.memberCount === 1 ? "member" : "members"} · @
          {group.ownerUsername}
        </span>
      </span>
      <span className={styles.badges}>
        {group.visibility === "private" ? <Badge>PRIVATE</Badge> : null}
        {group.isMember ? <Badge tone="cyan">MEMBER</Badge> : null}
      </span>
    </button>
  );
}
