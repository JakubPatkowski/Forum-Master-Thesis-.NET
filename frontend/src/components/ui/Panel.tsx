import type { ReactNode } from "react";

import styles from "./Panel.module.css";

export interface PanelProps {
  /** Mono, letter-spaced header label (e.g. "CATEGORIES", "ABOUT THREAD"). */
  label?: string;
  /** Header accent bar color. */
  accent?: "accent" | "cyan";
  /** Extra header content (e.g. a SOON badge), right-aligned. */
  headerExtra?: ReactNode;
  children: ReactNode;
  className?: string;
}

/** Sidebar/section panel: dark card + thin accent bar + mono label header. */
export function Panel({ label, accent = "accent", headerExtra, children, className }: PanelProps) {
  return (
    <section className={[styles.panel, className].filter(Boolean).join(" ")}>
      {label ? (
        <div className={styles.header}>
          <span className={accent === "cyan" ? styles.barCyan : styles.bar} />
          <span className={styles.label}>{label}</span>
          {headerExtra ? <span className={styles.extra}>{headerExtra}</span> : null}
        </div>
      ) : null}
      {children}
    </section>
  );
}
