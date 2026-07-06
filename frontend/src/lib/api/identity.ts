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
};
