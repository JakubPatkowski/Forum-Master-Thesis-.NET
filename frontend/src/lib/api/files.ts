import { apiFetch } from "@/lib/api/http";
import type {
  AttachFileRequest,
  CommitUploadResponse,
  FileDownloadResponse,
  FileTargetType,
  InitiateUploadRequest,
  InitiateUploadResponse,
} from "@/lib/api/types";

export const filesApi = {
  /** Step 1 of the direct-to-storage flow (ADR 0008): declare type+size, get a presigned PUT. */
  initiateUpload: (request: InitiateUploadRequest) =>
    apiFetch<InitiateUploadResponse>("/api/files", { method: "POST", body: request }),

  /**
   * Step 3: server stats the real bytes and sniffs the real type/dimensions. A 422
   * file.size_mismatch / file.type_mismatch means the declared metadata lied.
   * Idempotent — re-committing a committed file returns the same response.
   */
  commitUpload: (fileId: string) =>
    apiFetch<CommitUploadResponse>(`/api/files/${fileId}/commit`, { method: "POST" }),

  /** Pending (uncommitted) files 404. The returned presigned URL is short-lived. */
  getFile: (fileId: string) => apiFetch<FileDownloadResponse>(`/api/files/${fileId}`),

  listByTarget: (targetType: FileTargetType, targetId: string) =>
    apiFetch<FileDownloadResponse[]>(`/api/files?targetType=${targetType}&targetId=${targetId}`),

  /**
   * thread/comment/message: additive, cap 10. avatar/category_icon/thread_icon/group_icon:
   * replace. message attachments are sender-only; reading a message-attached file is gated
   * to conversation participants server-side (outsiders get 403, not a broken image).
   */
  attach: (fileId: string, request: AttachFileRequest) =>
    apiFetch(`/api/files/${fileId}/attachments`, { method: "POST", body: request }),

  detach: (fileId: string, targetType: FileTargetType, targetId: string) =>
    apiFetch(`/api/files/${fileId}/attachments?targetType=${targetType}&targetId=${targetId}`, {
      method: "DELETE",
    }),
};
