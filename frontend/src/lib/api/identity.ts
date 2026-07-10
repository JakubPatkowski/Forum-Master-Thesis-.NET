import { apiFetch } from "@/lib/api/http";
import type {
  AccessTokenResponse,
  LoginRequest,
  RegisterRequest,
  RegisterResponse,
} from "@/lib/api/types";

export const identityApi = {
  register: (request: RegisterRequest) =>
    apiFetch<RegisterResponse>("/api/identity/register", {
      method: "POST",
      body: request,
      auth: false,
    }),

  login: (request: LoginRequest) =>
    apiFetch<AccessTokenResponse>("/api/identity/login", {
      method: "POST",
      body: request,
      auth: false,
    }),

  /** Idempotent; also clears the refresh cookie server-side. */
  logout: () => apiFetch("/api/identity/logout", { method: "POST", auth: false }),

  /** Revokes every refresh token of the current user (bearer required). */
  logoutAll: () => apiFetch("/api/identity/logout-all", { method: "POST" }),

  // --- self-service account settings (all act on the caller's own account) ---

  changeUsername: (username: string) =>
    apiFetch("/api/identity/me/username", { method: "PATCH", body: { username } }),

  /** Requires the current password as confirmation (defense against hijacked sessions). */
  changeEmail: (email: string, currentPassword: string) =>
    apiFetch("/api/identity/me/email", { method: "PATCH", body: { email, currentPassword } }),

  /** On success the server revokes EVERY refresh token and clears the cookie — re-login required. */
  changePassword: (currentPassword: string, newPassword: string) =>
    apiFetch("/api/identity/me/password", {
      method: "POST",
      body: { currentPassword, newPassword },
    }),
};
