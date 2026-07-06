"use client";

/**
 * Tag autocomplete suggestions.
 *
 * MOCKED ON PURPOSE (scope decision): the backend has no tag list/search endpoint yet
 * (`GET /api/content/tags?query=` is proposed but unbuilt — tags only get-or-created as
 * a side effect of thread creation). The hook keeps the exact shape a future endpoint
 * would return, so swapping the mock for `useQuery(... contentApi.suggestTags(query))`
 * is a one-line change in this file. Tag CREATION via `tagSlugs` is real.
 */

import { useMemo } from "react";

export interface TagSuggestion {
  slug: string;
  name: string;
}

const MOCK_TAGS: TagSuggestion[] = [
  { slug: "home-lab", name: "home-lab" },
  { slug: "rust", name: "rust" },
  { slug: "dotnet", name: "dotnet" },
  { slug: "astro-imaging", name: "astro-imaging" },
  { slug: "3d-printing", name: "3d-printing" },
  { slug: "fermentation", name: "fermentation" },
  { slug: "mechanical-keyboards", name: "mechanical-keyboards" },
  { slug: "sensors", name: "sensors" },
  { slug: "arduino", name: "arduino" },
  { slug: "grafana", name: "grafana" },
];

export function useTagSuggestions(query: string, exclude: string[] = []) {
  return useMemo(() => {
    const q = query.trim().toLowerCase();
    return MOCK_TAGS.filter((t) => !exclude.includes(t.slug) && t.slug.includes(q)).slice(0, 5);
  }, [query, exclude]);
}
