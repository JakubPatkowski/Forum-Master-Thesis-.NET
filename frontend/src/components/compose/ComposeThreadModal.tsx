"use client";

/**
 * Create/edit thread modal (design: Compose.dc.html).
 *
 * Contract notes baked in:
 *  - create: POST /api/content/threads { categoryId, title, body, tagSlugs } → attach
 *    every READY upload to the new thread (targetType=thread) → navigate to it;
 *  - edit: PUT /api/content/threads/{id} accepts ONLY { title, body } — tags aren't
 *    editable after creation and the category select is disabled (moving a thread is a
 *    separate moderator-only PATCH, deliberately not wired into this composer);
 *  - inline images use the media convention (image:<fileId>) and are ALSO attached to
 *    the thread after create so the Files module tracks them (orphan sweep safety);
 *  - 422s land inline (field error or banner), 403/429 go to the toast sink.
 */

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";

import { AttachmentWidget } from "@/components/compose/AttachmentWidget";
import { MarkdownEditor } from "@/components/compose/MarkdownEditor";
import { TagPicker } from "@/components/compose/TagPicker";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { InlineErrorBanner } from "@/components/ui/ErrorState";
import { Modal } from "@/components/ui/Modal";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { ApiError } from "@/lib/api/problem";
import type { ThreadDetailResponse } from "@/lib/api/types";
import { useCategories, useCreateThread, useUpdateThread } from "@/lib/hooks/use-content";
import { useUploadManager } from "@/lib/upload/use-upload-manager";
import { MAX_ATTACHMENTS_PER_TARGET } from "@/lib/api/types";

import styles from "./ComposeThreadModal.module.css";

export interface ComposeThreadModalProps {
  mode: "create" | "edit";
  initialCategoryId?: string;
  thread?: ThreadDetailResponse;
  onClose: () => void;
}

export function ComposeThreadModal({
  mode,
  initialCategoryId,
  thread,
  onClose,
}: ComposeThreadModalProps) {
  const router = useRouter();
  const { showError, show } = useToast();
  const categories = useCategories();
  const uploads = useUploadManager(MAX_ATTACHMENTS_PER_TARGET);

  const [title, setTitle] = useState(thread?.title ?? "");
  const [body, setBody] = useState(thread?.body ?? "");
  const [categoryId, setCategoryId] = useState(initialCategoryId ?? thread?.categoryId ?? "");
  const [tags, setTags] = useState<string[]>(thread?.tags ?? []);
  const [titleError, setTitleError] = useState<string | null>(null);
  const [bannerError, setBannerError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const createThread = useCreateThread();
  const updateThread = useUpdateThread(thread?.id ?? "");

  const categoryOptions = useMemo(() => categories.data ?? [], [categories.data]);
  const effectiveCategoryId = categoryId || categoryOptions[0]?.id || "";

  const uploadInlineImage = async (file: File): Promise<string | null> => {
    const committed = await uploads.add(file);
    return committed?.fileId ?? null;
  };

  const submit = async () => {
    if (!title.trim()) {
      setTitleError("Title is required.");
      return;
    }
    setTitleError(null);
    setBannerError(null);
    setSubmitting(true);
    try {
      if (mode === "create") {
        const { threadId } = await createThread.mutateAsync({
          categoryId: effectiveCategoryId,
          title: title.trim(),
          body,
          tagSlugs: tags,
        });
        // Attach every committed upload so the Files module tracks them against the
        // thread (unattached files are orphan-swept server-side after a grace window).
        await Promise.all(
          uploads.readyFileIds.map((fileId) =>
            filesApi.attach(fileId, { targetType: "thread", targetId: threadId }).catch(() => {
              show("warning", "An image failed to attach", "The thread was created without it.");
            }),
          ),
        );
        onClose();
        router.push(`/t/${threadId}`);
      } else if (thread) {
        await updateThread.mutateAsync({ title: title.trim(), body });
        await Promise.all(
          uploads.readyFileIds.map((fileId) =>
            filesApi.attach(fileId, { targetType: "thread", targetId: thread.id }).catch(() => {
              show("warning", "An image failed to attach", "The edit was saved without it.");
            }),
          ),
        );
        onClose();
      }
    } catch (error) {
      if (error instanceof ApiError && error.errorType === "Validation") {
        setBannerError(error);
      } else {
        showError(error);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={mode === "edit" ? "Edit thread" : "New thread"}
      subtitle={
        mode === "edit"
          ? "PUT /api/content/threads/{id} · owner or moderator"
          : "POST /api/content/threads"
      }
      footer={
        <>
          <span className={styles.footerNote}>STORED AS RAW MARKDOWN · SANITIZED CLIENT-SIDE</span>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => void submit()} loading={submitting}>
            {mode === "edit" ? "Save changes" : "Publish thread"}
          </Button>
        </>
      }
    >
      {bannerError ? <InlineErrorBanner error={bannerError} /> : null}

      <Input
        label="Title"
        placeholder="A clear, specific question or statement"
        value={title}
        onChange={(e) => {
          setTitle(e.target.value);
          setTitleError(null);
        }}
        error={titleError}
      />

      <div className={styles.field}>
        <label className={styles.label}>Category</label>
        <select
          className={styles.select}
          value={effectiveCategoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          disabled={mode === "edit"}
        >
          {categoryOptions.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
              {category.visibility === "private" ? " — private" : ""}
            </option>
          ))}
        </select>
        {mode === "edit" ? (
          <span className={styles.editNote}>
            MOVING A THREAD USES PATCH /threads/&#123;id&#125;/category — MODERATOR ONLY
          </span>
        ) : null}
      </div>

      {mode === "create" ? (
        <TagPicker tags={tags} onChange={setTags} />
      ) : (
        <div className={styles.field}>
          <label className={styles.label}>
            Tags{" "}
            <Badge title="PUT /threads/{id} accepts only title+body — tags are set at creation">
              FIXED AT CREATION
            </Badge>
          </label>
          <div className={styles.tagList}>
            {thread?.tags.length ? (
              thread.tags.map((t) => (
                <span key={t} className={styles.tagStatic}>
                  #{t}
                </span>
              ))
            ) : (
              <span className={styles.editNote}>No tags.</span>
            )}
          </div>
        </div>
      )}

      <div className={styles.field}>
        <label className={styles.label}>
          Body <span className={styles.labelHint}>· markdown · insert media at the cursor</span>
        </label>
        <MarkdownEditor value={body} onChange={setBody} onUploadImage={uploadInlineImage} />
        <span className={styles.mediaNote}>
          IMAGE UPLOADS INSERT A TAG AT THE CURSOR — RENDERED INLINE WHERE YOU PLACE IT
        </span>
      </div>

      <AttachmentWidget
        entries={uploads.entries}
        onAdd={(f) => void uploads.add(f)}
        onRemove={uploads.remove}
      />
    </Modal>
  );
}
