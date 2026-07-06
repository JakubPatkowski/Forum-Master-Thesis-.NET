/**
 * Direct-to-storage upload (ADR 0008 / brief §4.7): the API only brokers metadata —
 * initiate (presigned PUT) → browser PUTs raw bytes straight to object storage →
 * commit (server stats + sniffs the real bytes). Bytes never touch our API.
 */

import { filesApi } from "@/lib/api/files";
import { ApiError } from "@/lib/api/problem";
import { ALLOWED_UPLOAD_TYPES, MAX_UPLOAD_BYTES, type CommitUploadResponse } from "@/lib/api/types";

export type UploadPhase =
  | { phase: "uploading"; progress: number }
  | { phase: "processing" }
  | { phase: "ready"; file: CommitUploadResponse }
  | { phase: "error"; error: ApiError };

/** PUTs the raw bytes to the presigned URL with progress (XHR: fetch has no upload progress). */
function putBytes(url: string, file: File, onProgress: (fraction: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", url);
    // The declared content type is part of what commit verifies against the real bytes.
    xhr.setRequestHeader("Content-Type", file.type);
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(event.loaded / event.total);
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) resolve();
      else reject(new ApiError(xhr.status, "Storage rejected the upload.", null, "Failure"));
    };
    xhr.onerror = () =>
      reject(new ApiError(0, "Upload failed — network error to storage.", null, "Network"));
    xhr.send(file);
  });
}

function friendlyCommitError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    // Declared metadata lied about the actual bytes — a specific, actionable message.
    if (error.code === "file.size_mismatch" || error.code === "file.type_mismatch") {
      return new ApiError(
        error.status,
        "The file doesn't match what was declared — pick it again and retry.",
        error.code,
        error.errorType,
      );
    }
    return error;
  }
  return new ApiError(0, "Upload failed.", null, "Unknown");
}

export function validateFile(file: File): ApiError | null {
  if (!ALLOWED_UPLOAD_TYPES.includes(file.type)) {
    return new ApiError(
      422,
      "Only PNG, JPEG, GIF and WebP images are allowed.",
      "file.type_not_allowed",
      "Validation",
    );
  }
  if (file.size > MAX_UPLOAD_BYTES) {
    return new ApiError(422, "Files may be at most 5 MiB.", "file.too_large", "Validation");
  }
  return null;
}

/** Full three-phase upload. `onPhase` receives every visible state transition. */
export async function uploadFile(
  file: File,
  onPhase: (phase: UploadPhase) => void,
): Promise<CommitUploadResponse> {
  const invalid = validateFile(file);
  if (invalid) {
    onPhase({ phase: "error", error: invalid });
    throw invalid;
  }

  try {
    onPhase({ phase: "uploading", progress: 0 });
    const initiated = await filesApi.initiateUpload({
      contentType: file.type,
      sizeBytes: file.size,
    });

    await putBytes(initiated.uploadUrl, file, (fraction) =>
      onPhase({ phase: "uploading", progress: fraction }),
    );

    onPhase({ phase: "processing" });
    const committed = await filesApi.commitUpload(initiated.fileId);
    onPhase({ phase: "ready", file: committed });
    return committed;
  } catch (error) {
    const friendly = friendlyCommitError(error);
    onPhase({ phase: "error", error: friendly });
    throw friendly;
  }
}
