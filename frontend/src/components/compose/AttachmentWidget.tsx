"use client";

/**
 * Upload widget with the three visible states from the design and brief §4.7:
 * UPLOADING (progress bar over the raw PUT) → PROCESSING (server-side commit
 * verification) → READY (attachable). Errors show the specific commit-mismatch copy.
 */

import { useRef, type ChangeEvent } from "react";

import { ALLOWED_UPLOAD_TYPES, MAX_ATTACHMENTS_PER_TARGET } from "@/lib/api/types";
import type { UploadEntry } from "@/lib/upload/use-upload-manager";
import { formatBytes } from "@/lib/utils/time";

import styles from "./AttachmentWidget.module.css";

export function AttachmentWidget({
  entries,
  onAdd,
  onRemove,
  maxFiles = MAX_ATTACHMENTS_PER_TARGET,
}: {
  entries: UploadEntry[];
  onAdd: (file: File) => void;
  onRemove: (localId: string) => void;
  maxFiles?: number;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const activeCount = entries.filter((e) => e.state.phase !== "error").length;

  const onPick = (event: ChangeEvent<HTMLInputElement>) => {
    for (const file of Array.from(event.target.files ?? [])) onAdd(file);
    event.target.value = "";
  };

  return (
    <div className={styles.widget}>
      <label className={styles.label}>
        Attachments{" "}
        <span className={styles.count}>
          · {activeCount}/{maxFiles} · images attach to the thread
        </span>
      </label>
      <div className={styles.list}>
        {entries.map((entry) => (
          <div key={entry.localId} className={styles.row}>
            <span className={styles.tile}>IMG</span>
            <div className={styles.info}>
              <span className={styles.name}>{entry.fileName}</span>
              {entry.state.phase === "uploading" ? (
                <span className={styles.track}>
                  <span
                    className={styles.bar}
                    style={{ width: `${Math.round(entry.state.progress * 100)}%` }}
                  />
                </span>
              ) : entry.state.phase === "error" ? (
                <span className={styles.errorText}>{entry.state.error.title}</span>
              ) : (
                <span className={styles.size}>{formatBytes(entry.sizeBytes)}</span>
              )}
            </div>
            {entry.state.phase === "uploading" ? (
              <span className={styles.stateUploading}>
                UPLOADING {Math.round(entry.state.progress * 100)}%
              </span>
            ) : entry.state.phase === "processing" ? (
              <span className={styles.stateProcessing}>PROCESSING</span>
            ) : entry.state.phase === "ready" ? (
              <span className={styles.stateReady}>READY</span>
            ) : (
              <span className={styles.stateError}>FAILED</span>
            )}
            <button
              type="button"
              className={styles.remove}
              onClick={() => onRemove(entry.localId)}
              title="Remove"
            >
              ×
            </button>
          </div>
        ))}
        <button
          type="button"
          className={styles.addButton}
          onClick={() => inputRef.current?.click()}
          disabled={activeCount >= maxFiles}
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
          >
            <path d="M12 5v14M5 12h14" />
          </svg>
          <span>ATTACH IMAGES · ≤5 MiB EACH · PNG/JPEG/GIF/WEBP</span>
        </button>
      </div>
      <input
        ref={inputRef}
        type="file"
        multiple
        accept={ALLOWED_UPLOAD_TYPES.join(",")}
        className={styles.hidden}
        onChange={onPick}
        aria-hidden
        tabIndex={-1}
      />
    </div>
  );
}
