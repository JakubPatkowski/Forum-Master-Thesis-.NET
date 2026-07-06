"use client";

/**
 * Left-rail category list (Home/Category pages): "All threads" entry + one row per
 * category, monogram tiles, PRIVATE badge for private categories (metadata only — never
 * a client-side visibility filter, brief §4.3).
 */

import Link from "next/link";

import { Badge } from "@/components/ui/Badge";
import { Monogram } from "@/components/ui/Monogram";
import { Panel } from "@/components/ui/Panel";
import { Skeleton } from "@/components/ui/Skeleton";
import { useCategories } from "@/lib/hooks/use-content";

import styles from "./CategorySidebar.module.css";

export function CategorySidebar({ activeSlug }: { activeSlug?: string }) {
  const categories = useCategories();

  return (
    <Panel label="CATEGORIES">
      <nav className={`${styles.list} panel-scroll`}>
        <Link href="/" className={activeSlug === undefined ? styles.rowActive : styles.row}>
          <span className={activeSlug === undefined ? styles.allTileActive : styles.allTile}>
            ✱
          </span>
          <span className={activeSlug === undefined ? styles.nameActive : styles.name}>
            All threads
          </span>
        </Link>
        {categories.isLoading ? (
          <div className={styles.loading}>
            <Skeleton height={30} />
            <Skeleton height={30} />
            <Skeleton height={30} />
          </div>
        ) : null}
        {categories.data?.map((category) => {
          const active = category.slug === activeSlug;
          return (
            <Link
              key={category.id}
              href={`/c/${category.slug}`}
              className={active ? styles.rowActive : styles.row}
            >
              <Monogram
                name={category.name}
                seed={category.slug}
                tone={active ? "accent" : undefined}
                active={active}
                size={30}
              />
              <span className={active ? styles.nameActive : styles.name}>{category.name}</span>
              {category.visibility === "private" ? <Badge>PRIVATE</Badge> : null}
            </Link>
          );
        })}
        {categories.data?.length === 0 ? (
          <div className={styles.empty}>No categories yet.</div>
        ) : null}
      </nav>
    </Panel>
  );
}
