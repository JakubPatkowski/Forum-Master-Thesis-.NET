"use client";

/**
 * Account settings — self-service username / email / password (backend: the
 * /api/identity/me/* endpoints; strictly own-account, no admin surface).
 *
 * Stale-claim handling: username/email live inside the access token's claims, so a
 * successful change immediately triggers the same silent refresh the 401-interceptor
 * uses — the UI re-derives the current user from the fresh token. A password change
 * revokes every refresh token server-side (and clears the cookie), so it instead drops
 * the local session and routes to /auth for a clean re-login.
 */

import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";

import { PageShell } from "@/components/layout/PageShell";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Panel } from "@/components/ui/Panel";
import { useToast } from "@/components/ui/toast";
import { identityApi } from "@/lib/api/identity";
import { ApiError } from "@/lib/api/problem";
import { useAuth } from "@/lib/auth/auth-context";
import { clearAccessToken, refreshAccessToken } from "@/lib/auth/token-store";

import styles from "./settings.module.css";

function UsernameForm({ current }: { current: string }) {
  const { show, showError } = useToast();
  const [username, setUsername] = useState(current);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    const trimmed = username.trim();
    if (trimmed.length < 3 || trimmed.length > 32) {
      setError("Username must be 3–32 characters.");
      return;
    }
    if (!/^[A-Za-z0-9_.-]+$/.test(trimmed)) {
      setError("Only letters, digits, '.', '_' and '-'.");
      return;
    }
    setBusy(true);
    try {
      await identityApi.changeUsername(trimmed);
      // Claims in the access token are now stale — mint a fresh one right away.
      await refreshAccessToken();
      show("success", "Username updated");
    } catch (err) {
      if (err instanceof ApiError && err.errorType === "Conflict") {
        setError(`${err.title} (409 · Conflict)`);
      } else {
        showError(err);
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Panel label="USERNAME">
      <form className={styles.form} onSubmit={(e) => void submit(e)}>
        <Input
          label="Username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          error={error}
          hint="3–32 chars: letters, digits, . _ -"
          autoComplete="username"
          required
        />
        <div className={styles.actions}>
          <Button type="submit" loading={busy} disabled={username.trim() === current}>
            Save username
          </Button>
        </div>
      </form>
    </Panel>
  );
}

function EmailForm({ current }: { current: string }) {
  const { show, showError } = useToast();
  const [email, setEmail] = useState(current);
  const [password, setPassword] = useState("");
  const [emailError, setEmailError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setEmailError(null);
    setPasswordError(null);
    setBusy(true);
    try {
      await identityApi.changeEmail(email.trim(), password);
      await refreshAccessToken();
      setPassword("");
      show("success", "Email updated");
    } catch (err) {
      if (err instanceof ApiError && err.code === "account.invalid_password") {
        setPasswordError("Current password is incorrect.");
      } else if (err instanceof ApiError && err.errorType === "Conflict") {
        setEmailError(`${err.title} (409 · Conflict)`);
      } else {
        showError(err);
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Panel label="EMAIL">
      <form className={styles.form} onSubmit={(e) => void submit(e)}>
        <Input
          type="email"
          label="Email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={emailError}
          autoComplete="email"
          required
        />
        <Input
          type="password"
          label="Current password"
          placeholder="Confirms it's really you"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={passwordError}
          autoComplete="current-password"
          required
        />
        <div className={styles.actions}>
          <Button type="submit" loading={busy}>
            Save email
          </Button>
        </div>
      </form>
    </Panel>
  );
}

function PasswordForm() {
  const router = useRouter();
  const { show, showError } = useToast();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [currentError, setCurrentError] = useState<string | null>(null);
  const [newError, setNewError] = useState<string | null>(null);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setCurrentError(null);
    setNewError(null);
    setConfirmError(null);
    if (newPassword.length < 8) {
      setNewError("Password must be at least 8 characters.");
      return;
    }
    if (confirm !== newPassword) {
      setConfirmError("Passwords don't match.");
      return;
    }
    setBusy(true);
    try {
      await identityApi.changePassword(currentPassword, newPassword);
      // Every refresh token is revoked and the cookie cleared — drop the local session too.
      clearAccessToken();
      show("success", "Password changed — log in again with the new one.");
      router.push("/auth");
    } catch (err) {
      if (err instanceof ApiError && err.code === "account.invalid_password") {
        setCurrentError("Current password is incorrect.");
      } else {
        showError(err);
      }
      setBusy(false);
    }
  };

  return (
    <Panel label="PASSWORD">
      <form className={styles.form} onSubmit={(e) => void submit(e)}>
        <Input
          type="password"
          label="Current password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          error={currentError}
          autoComplete="current-password"
          required
        />
        <Input
          type="password"
          label="New password"
          placeholder="Min. 8 characters"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          error={newError}
          autoComplete="new-password"
          required
        />
        <Input
          type="password"
          label="Repeat new password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          error={confirmError}
          autoComplete="new-password"
          required
        />
        <div className={styles.note}>
          Changing your password signs out every session — including this one.
        </div>
        <div className={styles.actions}>
          <Button type="submit" loading={busy}>
            Change password
          </Button>
        </div>
      </form>
    </Panel>
  );
}

export default function SettingsPage() {
  const router = useRouter();
  const { currentUser, isRestoring } = useAuth();

  useEffect(() => {
    if (!isRestoring && !currentUser) router.replace("/auth");
  }, [isRestoring, currentUser, router]);

  if (!currentUser) return null;

  return (
    <PageShell wide={false}>
      <div className={styles.wrap}>
        <header className={styles.header}>
          <h1 className={styles.title}>Account settings</h1>
          <div className={styles.subtitle}>
            @{currentUser.username} · changes apply to your own account only
          </div>
        </header>
        <UsernameForm key={`u-${currentUser.username}`} current={currentUser.username} />
        <EmailForm key={`e-${currentUser.email}`} current={currentUser.email} />
        <PasswordForm />
      </div>
    </PageShell>
  );
}
