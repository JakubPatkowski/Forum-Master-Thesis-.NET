/**
 * Minimal JWT payload decode — NOT verification (the server verifies; the client only
 * reads its own identity claims out of the access token it was just handed).
 * Claims (from the backend's JwtTokenService): sub = user id (ULID), name = username,
 * email, role = repeated global-role claim (user / moderator / admin).
 */

export interface CurrentUserClaims {
  id: string;
  username: string;
  email: string;
  roles: string[];
}

function base64UrlDecode(segment: string): string {
  const base64 = segment.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64 + "=".repeat((4 - (base64.length % 4)) % 4);
  const binary = atob(padded);
  // Interpret bytes as UTF-8 (usernames can contain non-ASCII).
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

export function decodeAccessToken(token: string): CurrentUserClaims | null {
  const segments = token.split(".");
  if (segments.length !== 3 || !segments[1]) return null;

  let payload: Record<string, unknown>;
  try {
    payload = JSON.parse(base64UrlDecode(segments[1])) as Record<string, unknown>;
  } catch {
    return null;
  }

  const id = typeof payload.sub === "string" ? payload.sub : null;
  if (!id) return null;

  const role = payload.role;
  const roles =
    typeof role === "string"
      ? [role]
      : Array.isArray(role)
        ? role.filter((r) => typeof r === "string")
        : [];

  return {
    id,
    username: typeof payload.name === "string" ? payload.name : "",
    email: typeof payload.email === "string" ? payload.email : "",
    roles,
  };
}
