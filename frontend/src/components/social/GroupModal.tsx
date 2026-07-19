"use client";

/**
 * Create/edit group modal — the CategoryModal pattern applied to Social groups.
 *
 * Contract notes baked in:
 *  - visibility affects DISCOVERY/JOIN only (public = open join, private = invite-only);
 *    members/chat are member-only either way.
 *  - icon: uploaded on pick (Files initiate → PUT → commit) and attached as
 *    targetType=group_icon (replace semantics) once the group id is known — after
 *    create for a new group, on save for an edit. Owner/admin only (server-enforced).
 */

import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";

import { Button } from "@/components/ui/Button";
import { GroupIcon } from "@/components/ui/GroupIcon";
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
  type GroupDetailResponse,
  type GroupVisibility,
} from "@/lib/api/types";
import { useCreateGroup, useUpdateGroup } from "@/lib/hooks/use-social";
import { uploadFile } from "@/lib/upload/upload";

import styles from "./GroupModal.module.css";

export interface GroupModalProps {
  mode: "create" | "edit";
  group?: GroupDetailResponse;
  onClose: () => void;
  /** Create only: select the fresh group in the caller's UI. */
  onCreated?: (groupId: string) => void;
}

export function GroupModal({ mode, group, onClose, onCreated }: GroupModalProps) {
  const queryClient = useQueryClient();
  const { showError, show } = useToast();
  const iconInputRef = useRef<HTMLInputElement>(null);

  const [name, setName] = useState(group?.name ?? "");
  const [description, setDescription] = useState(group?.description ?? "");
  const [visibility, setVisibility] = useState<GroupVisibility>(group?.visibility ?? "public");

  const [iconFileId, setIconFileId] = useState<string | null>(null);
  const [iconPreview, setIconPreview] = useState<string | null>(null);
  const [iconBusy, setIconBusy] = useState(false);

  const [nameError, setNameError] = useState<string | null>(null);
  const [bannerError, setBannerError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const createGroup = useCreateGroup();
  const updateGroup = useUpdateGroup(group?.groupId ?? "");

  useEffect(() => {
    if (!iconPreview) return;
    return () => URL.revokeObjectURL(iconPreview);
  }, [iconPreview]);

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

  // group_icon uses replace semantics server-side; failure is non-fatal (group saved).
  const attachIcon = async (groupId: string) => {
    if (!iconFileId) return;
    try {
      await filesApi.attach(iconFileId, { targetType: "group_icon", targetId: groupId });
      await queryClient.invalidateQueries({
        queryKey: queryKeys.filesByTarget("group_icon", groupId),
      });
    } catch {
      show("warning", "The icon didn't attach", "The group was saved without it.");
    }
  };

  const submit = async () => {
    const trimmedName = name.trim();
    if (trimmedName.length < 3) {
      setNameError("Name must be at least 3 characters.");
      return;
    }
    setBannerError(null);
    setSubmitting(true);
    try {
      const description_ = description.trim() || undefined;
      if (mode === "create") {
        const created = await createGroup.mutateAsync({
          name: trimmedName,
          description: description_,
          visibility,
        });
        await attachIcon(created.groupId);
        show("success", "Group created");
        onClose();
        onCreated?.(created.groupId);
      } else if (group) {
        await updateGroup.mutateAsync({
          name: trimmedName,
          description: description_,
          visibility,
        });
        await attachIcon(group.groupId);
        show("success", "Group updated");
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
      title={mode === "edit" ? "Edit group" : "New group"}
      subtitle={
        mode === "edit"
          ? "PUT /api/social/groups/{id} · owner or admin"
          : "POST /api/social/groups"
      }
      width={620}
      footer={
        <>
          <span className={styles.footerNote}>
            {visibility === "public" ? "PUBLIC · ANYONE CAN JOIN" : "PRIVATE · INVITE-ONLY"}
          </span>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={() => void submit()} loading={submitting}>
            {mode === "edit" ? "Save changes" : "Create group"}
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
          ) : mode === "edit" && group ? (
            <GroupIcon groupId={group.groupId} name={group.name} size={56} />
          ) : (
            <Monogram name={name || "?"} seed={name || "group"} size={56} />
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
        placeholder="e.g. Home Lab Crew"
        value={name}
        onChange={(e) => {
          setName(e.target.value);
          setNameError(null);
        }}
        error={nameError}
      />

      <Textarea
        label="Description"
        placeholder="What is this group about? (optional)"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        rows={3}
      />

      <div className={styles.field}>
        <label className={styles.label} htmlFor="group-visibility">
          Visibility
        </label>
        <select
          id="group-visibility"
          className={styles.select}
          value={visibility}
          onChange={(e) => setVisibility(e.target.value as GroupVisibility)}
        >
          <option value="public">public — listed, anyone can join</option>
          <option value="private">private — invite-only, hidden from discovery</option>
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
