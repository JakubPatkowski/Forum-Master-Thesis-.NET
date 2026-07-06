"use client";

/**
 * Tracks a list of in-flight/finished uploads for the composer's attachment widget —
 * each entry walks the three visible states (uploading → processing → ready) or lands
 * in error. Attaching to a target happens separately, after the target exists.
 */

import { useCallback, useRef, useState } from "react";

import type { CommitUploadResponse } from "@/lib/api/types";
import { uploadFile, type UploadPhase } from "@/lib/upload/upload";

export interface UploadEntry {
  /** Local id for list rendering (files get their real ULID only after initiate). */
  localId: string;
  fileName: string;
  sizeBytes: number;
  state: UploadPhase;
}

export function useUploadManager(maxFiles: number) {
  const [entries, setEntries] = useState<UploadEntry[]>([]);
  const counter = useRef(0);

  const patchEntry = useCallback((localId: string, state: UploadPhase) => {
    setEntries((list) => list.map((e) => (e.localId === localId ? { ...e, state } : e)));
  }, []);

  const add = useCallback(
    async (file: File): Promise<CommitUploadResponse | null> => {
      let allowed = true;
      setEntries((list) => {
        const readyOrBusy = list.filter((e) => e.state.phase !== "error");
        if (readyOrBusy.length >= maxFiles) {
          allowed = false;
          return list;
        }
        return list;
      });
      if (!allowed) return null;

      counter.current += 1;
      const localId = `upload-${counter.current}`;
      setEntries((list) => [
        ...list,
        {
          localId,
          fileName: file.name,
          sizeBytes: file.size,
          state: { phase: "uploading", progress: 0 },
        },
      ]);

      try {
        return await uploadFile(file, (state) => patchEntry(localId, state));
      } catch {
        return null; // the entry already shows its error state
      }
    },
    [maxFiles, patchEntry],
  );

  const remove = useCallback((localId: string) => {
    setEntries((list) => list.filter((e) => e.localId !== localId));
  }, []);

  const clear = useCallback(() => setEntries([]), []);

  const readyFileIds = entries.flatMap((e) =>
    e.state.phase === "ready" ? [e.state.file.fileId] : [],
  );

  return { entries, add, remove, clear, readyFileIds };
}
