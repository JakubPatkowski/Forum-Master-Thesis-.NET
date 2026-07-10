"use client";

/**
 * Left-rail category list (Home/Category pages): "All threads" entry + one row per
 * category, monogram tiles, PRIVATE badge for private categories (metadata only — never
 * a client-side visibility filter, brief §4.3).
 */

import Link from "next/link";

import { useCategoryModal } from "@/components/category/category-context";
import { Badge } from "@/components/ui/Badge";
import { CategoryIcon } from "@/components/ui/CategoryIcon";
import { Panel } from "@/components/ui/Panel";
import { Skeleton } from "@/components/ui/Skeleton";
import { useAuth } from "@/lib/auth/auth-context";
import { useCategories } from "@/lib/hooks/use-content";

import styles from "./CategorySidebar.module.css";

export function CategorySidebar({ activeSlug }: { activeSlug?: string }) {
  const categories = useCategories();
  const { isAuthenticated } = useAuth();
  const { openCreateCategory } = useCategoryModal();

  return (
    <Panel
      label="CATEGORIES"
      headerExtra={
        isAuthenticated ? (
          <button
            type="button"
            className={styles.addButton}
            onClick={openCreateCategory}
            title="New category"
            aria-label="New category"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.2"
            >
              <path d="M12 5v14M5 12h14" />
            </svg>
          </button>
        ) : undefined
      }
    >
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
              <CategoryIcon
                categoryId={category.id}
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
