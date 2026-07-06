import type { ReactNode } from "react";

import { TopNav } from "@/components/layout/TopNav";

import styles from "./PageShell.module.css";

/**
 * Page chrome shared by every route: the fixed cyan grid texture + fade (the design's
 * background greeble), TopNav, and a max-width content container.
 */
export function PageShell({ children, wide = true }: { children: ReactNode; wide?: boolean }) {
  return (
    <div className={styles.shell}>
      <div className={styles.grid} aria-hidden />
      <div className={styles.fade} aria-hidden />
      <div className={styles.content}>
        <TopNav />
        <div className={wide ? styles.container : styles.containerNarrow}>{children}</div>
      </div>
    </div>
  );
}
