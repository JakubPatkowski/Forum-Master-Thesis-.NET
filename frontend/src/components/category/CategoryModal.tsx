"use client";

/**
 * Create/edit category modal. Backed by POST/PUT /api/content/categories.
 *
 * Contract notes baked in:
 *  - create: needs the global `create` permission (same bit as posting a thread); the
 *    creator becomes owner. The slug is the URL identifier — set once, fixed after.
 *  - edit: PUT /api/content/categories/{slug} accepts name/description/visibility only
 *    (owner or moderator); the slug is immutable, shown read-only.
 *  - icon: the image is uploaded on pick (Files initiate → PUT → commit) and attached as
 *    targetType=category_icon (replace semantics) once the category id is known — after
 *    create for a new category, immediately for an edit.
 *  - 409 category.slug_taken lands on the slug field; 422s in the banner; 403/429 → toast.
 */

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";

import { Button } from "@/components/ui/Button";
import { CategoryIcon } from "@/components/ui/CategoryIcon";
import { InlineErrorBanner } from "@/components/ui/ErrorState";
import { Input, Textarea } from "@/components/ui/Input";
import { Modal } from "@/components/ui/Modal";
import { Monogram } from "@/components/ui/Monogram";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { ApiError } from "@/lib/api/problem";
import {
  ALLOWED_UPLOAD_TYPES,
  type CategoryResponse,
  type CategoryVisibility,
} from "@/lib/api/types";
import { useCreateCategory, useUpdateCategory } from "@/lib/hooks/use-content";
import { uploadFile } from "@/lib/upload/upload";

import styles from "./CategoryModal.module.css";

const SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** Best-effort slug from a display name; the user can always override it. */
function slugify(value: string): string {
  return value
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64);
}

export interface CategoryModalProps {
  mode: "create" | "edit";
  category?: CategoryResponse;
  onClose: () => void;
}

export function CategoryModal({ mode, category, onClose }: CategoryModalProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { showError, show } = useToast();
  const iconInputRef = useRef<HTMLInputElement>(null);

  const [name, setName] = useState(category?.name ?? "");
  const [slug, setSlug] = useState(category?.slug ?? "");
  // Until the slug is hand-edited it mirrors the name (create only; edit locks it).
  const [slugTouched, setSlugTouched] = useState(mode === "edit");
  const [description, setDescription] = useState(category?.description ?? "");
  const [visibility, setVisibility] = useState<CategoryVisibility>(
    category?.visibility ?? "public",
  );

  const [iconFileId, setIconFileId] = useState<string | null>(null);
  const [iconPreview, setIconPreview] = useState<string | null>(null);
  const [iconBusy, setIconBusy] = useState(false);

  const [nameError, setNameError] = useState<string | null>(null);
  const [slugError, setSlugError] = useState<string | null>(null);
  const [bannerError, setBannerError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory(category?.slug ?? "");

  // Revoke the object URL when it changes or the modal unmounts (no memory leak).
  useEffect(() => {
    if (!iconPreview) return;
    return () => URL.revokeObjectURL(iconPreview);
  }, [iconPreview]);

  const onNameChange = (value: string) => {
    setName(value);
    setNameError(null);
    if (!slugTouched) {
      setSlug(slugify(value));
      setSlugError(null);
    }
  };

  const onPickIcon = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setIconBusy(true);
    try {
      const committed = await uploadFile(file, () => {});
      setIconFileId(committed.fileId);
      setIconPreview(URL.createObjectURL(file));
    } catch (error) {
      showError(error);
    } finally {
      setIconBusy(false);
    }
  };

  // Attaching a category_icon uses replace semantics server-side (old one detached, then
  // orphan-swept). Failure here is non-fatal: the category itself was already saved.
  const attachIcon = async (categoryId: string) => {
    if (!iconFileId) return;
    try {
      await filesApi.attach(iconFileId, { targetType: "category_icon", targetId: categoryId });
      await queryClient.invalidateQueries({
        queryKey: queryKeys.filesByTarget("category_icon", categoryId),
      });
    } catch {
      show("warning", "The icon didn't attach", "The category was saved without it.");
    }
  };

  const submit = async () => {
    const trimmedName = name.trim();
    if (trimmedName.length < 3) {
      setNameError("Name must be at least 3 characters.");
      return;
    }
    if (mode === "create" && !SLUG_RE.test(slug)) {
      setSlugError("Lower-case letters, digits and single dashes only (3–64 chars).");
      return;
    }
    setBannerError(null);
    setSubmitting(true);
    try {
      const description_ = description.trim() || undefined;
      if (mode === "create") {
        const created = await createCategory.mutateAsync({
          slug,
          name: trimmedName,
          description: description_,
          visibility,
        });
        await attachIcon(created.categoryId);
        show("success", "Category created");
        onClose();
        router.push(`/c/${created.slug}`);
      } else if (category) {
        await updateCategory.mutateAsync({
          name: trimmedName,
          description: description_,
          visibility,
        });
        await attachIcon(category.id);
        show("success", "Category updated");
        onClose();
      }
    } catch (error) {
      if (error instanceof ApiError && error.code === "category.slug_taken") {
        setSlugError("That slug is already taken.");
      } else if (error instanceof ApiError && error.errorType === "Validation") {
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
      title={mode === "edit" ? "Edit category" : "New category"}
      subtitle={
        mode === "edit"
          ? "PUT /api/content/categories/{slug} · owner or moderator"
          : "POST /api/content/categories"
      }
      width={620}
      footer={
        <>
          <span className={styles.footerNote}>
            {mode === "edit" ? "SLUG IS FIXED · SET AT CREATION" : "SLUG BECOMES THE CATEGORY URL"}
          </span>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => void submit()} loading={submitting}>
            {mode === "edit" ? "Save changes" : "Create category"}
          </Button>
        </>
      }
    >
      {bannerError ? <InlineErrorBanner error={bannerError} /> : null}

      <div className={styles.iconRow}>
        <div className={styles.iconTile}>
          {iconPreview ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img className={styles.iconImage} src={iconPreview} alt="" width={56} height={56} />
          ) : mode === "edit" && category ? (
            <CategoryIcon
              categoryId={category.id}
              name={category.name}
              seed={category.slug}
              size={56}
            />
          ) : (
            <Monogram name={name || "?"} seed={slug || name || "category"} size={56} />
          )}
        </div>
        <div className={styles.iconMeta}>
          <span className={styles.label}>Icon</span>
          <span className={styles.iconHint}>
            PNG/JPEG/GIF/WEBP · ≤5 MiB · replaces the monogram tile
          </span>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => iconInputRef.current?.click()}
            loading={iconBusy}
          >
            {iconFileId ? "Choose a different image" : "Upload icon"}
          </Button>
        </div>
      </div>

      <Input
        label="Name"
        placeholder="e.g. Home Lab"
        value={name}
        onChange={(e) => onNameChange(e.target.value)}
        error={nameError}
      />

      {mode === "create" ? (
        <Input
          label="Slug"
          hint="Lower-case letters, digits and single dashes. This becomes the category's URL."
          placeholder="home-lab"
          value={slug}
          onChange={(e) => {
            setSlugTouched(true);
            setSlug(e.target.value);
            setSlugError(null);
          }}
          error={slugError}
        />
      ) : (
        <div className={styles.field}>
          <span className={styles.label}>Slug</span>
          <div className={styles.slugStatic}>/{category?.slug}</div>
          <span className={styles.editNote}>The slug is fixed after creation.</span>
        </div>
      )}

      <Textarea
        label="Description"
        placeholder="What belongs in this category? (optional)"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        rows={3}
      />

      <div className={styles.field}>
        <label className={styles.label} htmlFor="category-visibility">
          Visibility
        </label>
        <select
          id="category-visibility"
          className={styles.select}
          value={visibility}
          onChange={(e) => setVisibility(e.target.value as CategoryVisibility)}
        >
          <option value="public">public — anyone can read and post</option>
          <option value="private">private — only owner &amp; moderators post</option>
        </select>
      </div>

      <input
        ref={iconInputRef}
        type="file"
        accept={ALLOWED_UPLOAD_TYPES.join(",")}
        className={styles.hidden}
        onChange={(e) => void onPickIcon(e)}
        aria-hidden
        tabIndex={-1}
      />
    </Modal>
  );
}
