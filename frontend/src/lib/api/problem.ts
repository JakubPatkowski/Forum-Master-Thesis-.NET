/**
 * The single place a failed response becomes a typed error (brief §4.1).
 *
 * Normal shape is RFC7807 ProblemDetails with `title` (human message), `code` (stable
 * machine code) and `errorType` (NotFound | Forbidden | Unauthorized | Conflict |
 * Validation | Failure). Known inconsistencies handled here:
 *   - 429 has an EMPTY body — copy is keyed off the status alone.
 *   - a few auth/admin 401/403s omit `code`/`errorType` entirely.
 *   - one admin endpoint returns a bespoke `{ "error": "..." }` with 400.
 */

export type ApiErrorType =
  | "NotFound"
  | "Forbidden"
  | "Unauthorized"
  | "Conflict"
  | "Validation"
  | "TooManyRequests"
  | "Failure"
  | "Network"
  | "Unknown";

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly errorType: ApiErrorType;
  readonly title: string;

  constructor(status: number, title: string, code: string | null, errorType: ApiErrorType) {
    super(title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.code = code;
    this.errorType = errorType;
  }
}

function errorTypeFromStatus(status: number): ApiErrorType {
  switch (status) {
    case 404:
      return "NotFound";
    case 403:
      return "Forbidden";
    case 401:
      return "Unauthorized";
    case 409:
      return "Conflict";
    case 422:
      return "Validation";
    case 429:
      return "TooManyRequests";
    default:
      return status >= 500 ? "Failure" : "Unknown";
  }
}

function defaultTitle(status: number): string {
  switch (status) {
    case 404:
      return "Not found.";
    case 403:
      return "You don't have permission to do that.";
    case 401:
      return "You need to be logged in.";
    case 409:
      return "That conflicts with something that already exists.";
    case 422:
      return "That input isn't valid.";
    case 429:
      return "Too many requests — try again in a moment.";
    default:
      return "Something broke on our side.";
  }
}

const KNOWN_ERROR_TYPES: ReadonlySet<string> = new Set([
  "NotFound",
  "Forbidden",
  "Unauthorized",
  "Conflict",
  "Validation",
  "Failure",
]);

/** Parses an already-read body; exported separately so it is unit-testable without fetch. */
export function problemFromBody(status: number, rawBody: string): ApiError {
  // 429 (and any other empty body): status-derived generic error.
  if (rawBody.trim() === "") {
    return new ApiError(status, defaultTitle(status), null, errorTypeFromStatus(status));
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(rawBody);
  } catch {
    return new ApiError(status, defaultTitle(status), null, errorTypeFromStatus(status));
  }

  if (typeof parsed !== "object" || parsed === null) {
    return new ApiError(status, defaultTitle(status), null, errorTypeFromStatus(status));
  }

  const body = parsed as Record<string, unknown>;

  // Bespoke admin shape: { "error": "Invalid scope id." } with 400.
  if (typeof body.error === "string" && body.error.length > 0) {
    return new ApiError(status, body.error, null, errorTypeFromStatus(status));
  }

  const title =
    typeof body.title === "string" && body.title.length > 0 ? body.title : defaultTitle(status);
  const code = typeof body.code === "string" && body.code.length > 0 ? body.code : null;
  const errorType =
    typeof body.errorType === "string" && KNOWN_ERROR_TYPES.has(body.errorType)
      ? (body.errorType as ApiErrorType)
      : errorTypeFromStatus(status);

  return new ApiError(status, title, code, errorType);
}

export async function problemFromResponse(response: Response): Promise<ApiError> {
  let raw = "";
  try {
    raw = await response.text();
  } catch {
    // fall through to the empty-body path
  }
  return problemFromBody(response.status, raw);
}
