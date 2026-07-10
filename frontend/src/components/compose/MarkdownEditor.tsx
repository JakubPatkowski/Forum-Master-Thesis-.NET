"use client";

/**
 * Markdown editor with the design's toolbar, WRITE/PREVIEW tabs, and cursor-anchored
 * media insertion (design: Compose.dc.html / Thread.dc.html composer).
 *
 * IMAGE uploads the picked file through the real Files flow (initiate → PUT → commit)
 * and inserts `![caption](image:<fileId>)` at the cursor. VIDEO inserts the `@video()`
 * token of the frontend media convention (see lib/markdown/media-convention.ts — the
 * backend accepts only images today, so the ref is typically an https URL for now).
 * PREVIEW renders through the real sanitizing renderer — what you see is what readers get.
 */

import { useRef, useState, type ChangeEvent } from "react";

import { MarkdownView } from "@/components/markdown/MarkdownView";
import { ALLOWED_UPLOAD_TYPES } from "@/lib/api/types";
import { imageToken } from "@/lib/markdown/media-convention";

import styles from "./MarkdownEditor.module.css";

export interface MarkdownEditorProps {
  value: string;
  onChange: (value: string) => void;
  rows?: number;
  placeholder?: string;
  /** Uploads a picked image and resolves its committed fileId (null on failure). */
  onUploadImage?: (file: File) => Promise<string | null>;
  compact?: boolean;
}

export function MarkdownEditor({
  value,
  onChange,
  rows = 12,
  placeholder = "Raw Markdown. Rendered & sanitized on display.",
  onUploadImage,
  compact = false,
}: MarkdownEditorProps) {
  const areaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [view, setView] = useState<"write" | "preview">("write");
  const [uploadingInline, setUploadingInline] = useState(false);

  const insertAtCursor = (before: string, after: string, placeholderText: string) => {
    const el = areaRef.current;
    let start = value.length;
    let end = value.length;
    if (el && typeof el.selectionStart === "number") {
      start = el.selectionStart;
      end = el.selectionEnd;
    }
    const selected = value.slice(start, end) || placeholderText;
    const next = value.slice(0, start) + before + selected + after + value.slice(end);
    const caretStart = start + before.length;
    const caretEnd = caretStart + selected.length;
    onChange(next);
    setView("write");
    setTimeout(() => {
      const el2 = areaRef.current;
      if (el2) {
        el2.focus();
        el2.setSelectionRange(caretStart, caretEnd);
      }
    }, 0);
  };

  const onPickImage = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file || !onUploadImage) return;
    setUploadingInline(true);
    try {
      const fileId = await onUploadImage(file);
      if (fileId) {
        insertAtCursor(`\n${imageToken(fileId, file.name)}\n`, "", "");
      }
    } finally {
      setUploadingInline(false);
    }
  };

  const tool = (label: string, title: string, action: () => void, extraClass?: string) => (
    <button
      key={title}
      type="button"
      className={extraClass ? `${styles.tool} ${extraClass}` : styles.tool}
      title={title}
      onClick={action}
    >
      {label}
    </button>
  );

  return (
    <div className={styles.editor}>
      <div className={styles.toolbar}>
        {tool("B", "Bold", () => insertAtCursor("**", "**", "bold text"), styles.bold)}
        {tool("I", "Italic", () => insertAtCursor("*", "*", "italic"), styles.italic)}
        {tool(
          "S",
          "Strikethrough",
          () => insertAtCursor("~~", "~~", "strikethrough"),
          styles.strike,
        )}
        {tool("H", "Heading", () => insertAtCursor("\n## ", "", "Heading"), styles.bold)}
        <span className={styles.divider} />
        {tool("•", "Bullet list", () => insertAtCursor("\n- ", "", "List item"))}
        {tool("1.", "Numbered list", () => insertAtCursor("\n1. ", "", "List item"))}
        {tool('"', "Quote", () => insertAtCursor("\n> ", "", "Quote"))}
        {tool("</>", "Code", () =>
          compact ? insertAtCursor("`", "`", "code") : insertAtCursor("\n```\n", "\n```\n", "code"),
        )}
        {tool("🔗", "Link", () => insertAtCursor("[", "](https://)", "label"))}
        <span className={styles.divider} />
        {onUploadImage ? (
          <button
            type="button"
            className={styles.mediaTool}
            title="Upload an image and insert it at the cursor"
            onClick={() => fileInputRef.current?.click()}
            disabled={uploadingInline}
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor">
              <path d="M21 19V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2zM8.5 11 11 14l3.5-4.5L19 16H5l3.5-5z" />
            </svg>
            {uploadingInline ? "UPLOADING…" : "IMAGE"}
          </button>
        ) : null}
        <button
          type="button"
          className={styles.mediaTool}
          title="Insert a video token at the cursor (uploads are images-only today — use an https URL)"
          onClick={() => insertAtCursor("\n@video(", ")\n", "https://")}
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor">
            <path d="M4 4h11a1 1 0 0 1 1 1v3l4-2v12l-4-2v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z" />
          </svg>
          VIDEO
        </button>
        <span className={styles.spacer} />
        <button
          type="button"
          className={view === "write" ? styles.tabActive : styles.tab}
          onClick={() => setView("write")}
        >
          WRITE
        </button>
        <button
          type="button"
          className={view === "preview" ? styles.tabActive : styles.tab}
          onClick={() => setView("preview")}
        >
          PREVIEW
        </button>
      </div>

      {view === "write" ? (
        <textarea
          ref={areaRef}
          className={styles.textarea}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          rows={rows}
          placeholder={placeholder}
        />
      ) : (
        <div className={styles.preview}>
          {value.trim() ? (
            <MarkdownView markdown={value} />
          ) : (
            <span className={styles.previewEmpty}>Nothing to preview yet.</span>
          )}
        </div>
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept={ALLOWED_UPLOAD_TYPES.join(",")}
        className={styles.hiddenInput}
        onChange={onPickImage}
        aria-hidden
        tabIndex={-1}
      />
    </div>
  );
}
