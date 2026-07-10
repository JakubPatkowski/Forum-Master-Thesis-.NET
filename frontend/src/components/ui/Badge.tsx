import type { ReactNode } from "react";

import styles from "./Badge.module.css";

export interface BadgeProps {
  tone?: "neutral" | "accent" | "cyan" | "warning" | "error";
  children: ReactNode;
  title?: string;
  className?: string;
}

/** Small mono, letter-spaced chip — PINNED / PRIVATE / EDITED / OP / YOU / SOON / NEW … */
export function Badge({ tone = "neutral", children, title, className }: BadgeProps) {
  return (
    <span
      className={[styles.badge, styles[tone], className].filter(Boolean).join(" ")}
      title={title}
    >
      {children}
    </span>
  );
}
