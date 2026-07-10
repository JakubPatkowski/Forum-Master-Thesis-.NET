"use client";

/**
 * Tag suggestions backed by the real GET /api/content/tags: most-used tags when the
 * query is empty (popular-tags panel), debounced substring match on slug while typing
 * (compose autocomplete). Tag CREATION via `tagSlugs` on thread create stays a separate
 * flow — suggestions are never required to submit a new slug.
 */

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { contentApi } from "@/lib/api/content";
import { queryKeys } from "@/lib/api/keys";
import type { TagSuggestionResponse } from "@/lib/api/types";

export type TagSuggestion = TagSuggestionResponse;

const DEBOUNCE_MS = 250;

export function useTagSuggestions(query: string, exclude: string[] = []) {
  const q = query.trim().toLowerCase();
  const [debounced, setDebounced] = useState(q);

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(q), DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [q]);

  const suggestions = useQuery({
    queryKey: queryKeys.tagSuggestions(debounced),
    queryFn: () => contentApi.suggestTags(debounced),
    staleTime: 60_000,
    // Keep the previous list visible while the debounced re-fetch is in flight.
    placeholderData: (previous: TagSuggestionResponse[] | undefined) => previous,
  });

  return useMemo(() => {
    const rows = suggestions.data ?? [];
    return rows.filter((tag) => !exclude.includes(tag.slug)).slice(0, debounced ? 5 : 10);
  }, [suggestions.data, exclude, debounced]);
}
