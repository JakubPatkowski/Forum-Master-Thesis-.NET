"use client";

/**
 * Renders one inline-media token from the markdown convention. A file ref resolves via
 * GET /api/files/{id} (cached; presigned URLs are short-lived so the query stays fresh-
 * ish); an https ref (videos only, forward-compat) is used directly. Unresolvable refs
 * degrade to a labelled placeholder instead of a broken element.
 */

import { useQuery } from "@tanstack/react-query";

import { queryKeys } from "@/lib/api/keys";
import { staleTimes } from "@/lib/api/stale-times";
import { filesApi } from "@/lib/api/files";
import { isFileRef } from "@/lib/markdown/media-convention";

import styles from "./InlineMedia.module.css";

interface InlineMediaProps {
  kind: "image" | "video";
  mediaRef: string;
  caption?: string;
}

export function InlineMedia({ kind, mediaRef, caption }: InlineMediaProps) {
  const fileBacked = isFileRef(mediaRef);
  const file = useQuery({
    queryKey: queryKeys.file(mediaRef),
    queryFn: () => filesApi.getFile(mediaRef),
    enabled: fileBacked,
    staleTime: staleTimes.presignedFiles,
    retry: false,
  });

  const externalVideo = kind === "video" && /^https:\/\//i.test(mediaRef) ? mediaRef : null;
  const url = externalVideo ?? file.data?.url ?? null;

  if (fileBacked && file.isLoading) {
    return <span className={styles.placeholder}>loading media…</span>;
  }

  if (!url) {
    return (
      <span className={styles.unavailable} title={mediaRef}>
        {kind === "image" ? "image unavailable" : "video unavailable"}
      </span>
    );
  }

  if (kind === "video") {
    return (
      <span className={styles.figure}>
        <video className={styles.media} src={url} controls preload="metadata" />
        {caption ? <span className={styles.caption}>{caption}</span> : null}
      </span>
    );
  }

  return (
    <span className={styles.figure}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img className={styles.media} src={url} alt={caption ?? ""} loading="lazy" />
      {caption ? <span className={styles.caption}>{caption}</span> : null}
    </span>
  );
}
