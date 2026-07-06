"use client";

/**
 * POPULAR TAGS rail. Mock data behind a SOON badge — there is no tag list endpoint yet
 * (same gap as the TagPicker suggestions; see lib/hooks/use-tag-suggestions.ts).
 */

import { Badge } from "@/components/ui/Badge";
import { Panel } from "@/components/ui/Panel";
import { TagChip } from "@/components/ui/TagChip";
import { useTagSuggestions } from "@/lib/hooks/use-tag-suggestions";

import styles from "./panels.module.css";

export function PopularTagsPanel() {
  const tags = useTagSuggestions("");
  return (
    <Panel
      label="POPULAR TAGS"
      headerExtra={
        <Badge
          tone="warning"
          title="Requires GET /api/content/tags — endpoint does not exist yet; mock data"
        >
          SOON
        </Badge>
      }
    >
      <div className={styles.tagCloud}>
        {tags.map((tag) => (
          <TagChip key={tag.slug} slug={tag.slug} />
        ))}
      </div>
    </Panel>
  );
}
