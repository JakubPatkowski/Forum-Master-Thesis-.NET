/**
 * Query freshness tiers (10d audit). staleTime decides ONE thing here: whether navigating
 * back to an already-cached view triggers a background refetch. While a view is mounted,
 * connected and subscribed, the WebSocket push-invalidation (lib/realtime/invalidation.ts)
 * is what keeps realtime-covered data fresh — staleTime plays no part in that — and every
 * (re)connect resyncs covered queries wholesale, so a long staleTime does not extend the
 * staleness window for anything the push feed covers.
 *
 * Not Infinity for the covered tier, deliberately: unsubscribing (unmount) also stops the
 * pushes, so a view revisited within gcTime could otherwise render an arbitrarily old
 * cache with nothing ever refreshing it. Five minutes bounds that gap.
 */
export const staleTimes = {
  /**
   * Thread feeds/details, comment trees, reactions — everything
   * lib/realtime/invalidation.ts patches from WS notifications.
   */
  realtimeCovered: 5 * 60_000,

  /**
   * Anything whose payload embeds a presigned MinIO URL (avatars, icons, attachments,
   * inline media). MUST stay comfortably below the backend's Files:DownloadUrlTtlMinutes
   * (15 min) or a cached entry can hand an expired URL to an <img> that renders later.
   */
  presignedFiles: 5 * 60_000,

  /** Categories & tags: no realtime coverage (no CategoryCreated event), so stay modest. */
  reference: 60_000,

  /** Search results: not push-covered, re-running a search is cheap and rare. */
  search: 30_000,
} as const;
