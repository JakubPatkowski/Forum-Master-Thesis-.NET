import type { SearchSort } from "@/lib/api/types";

/**
 * The search page's sort state (A/B v5). Newest is the page default, like Architecture B's search page; the
 * choice travels in the URL (?sort=) so a shared link reproduces it, and the default is left out of the URL so plain
 * links stay short. (Kept outside the page module: Next.js pages may only export the route's own members.)
 */
export const DEFAULT_SEARCH_SORT: SearchSort = "newest";

/** The ?sort= value, or the page default for anything missing or unknown. */
export function parseSearchSort(value: string | null | undefined): SearchSort {
  return value === "relevance" ? "relevance" : DEFAULT_SEARCH_SORT;
}

/** The shareable URL of a search state. */
export function searchUrl(query: string, sort: SearchSort): string {
  const params = new URLSearchParams();
  if (query) params.set("q", query);
  if (sort !== DEFAULT_SEARCH_SORT) params.set("sort", sort);
  const qs = params.toString();
  return qs ? `/search?${qs}` : "/search";
}
