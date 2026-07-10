"use client";

/**
 * POPULAR TAGS rail — real usage-ranked tags from GET /api/content/tags (empty query =
 * most-used first; see lib/hooks/use-tag-suggestions.ts).
 */

import { Panel } from "@/components/ui/Panel";
import { TagChip } from "@/components/ui/TagChip";
import { useTagSuggestions } from "@/lib/hooks/use-tag-suggestions";

import styles from "./panels.module.css";

export function PopularTagsPanel() {
  const tags = useTagSuggestions("");
  if (tags.length === 0) return null;
  return (
    <Panel label="POPULAR TAGS">
      <div className={styles.tagCloud}>
        {tags.map((tag) => (
          <TagChip key={tag.slug} slug={tag.slug} />
        ))}
      </div>
    </Panel>
  );
}
