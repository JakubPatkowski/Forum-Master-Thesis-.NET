"use client";

/**
 * Login / Register (design: Auth.dc.html). A 401 login failure surfaces as the banner
 * (the API's non-revealing "invalid email or password"); a 409 username/email conflict
 * maps to an inline field error. Register chains into login for a smooth first session.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState, type FormEvent } from "react";

import { PageShell } from "@/components/layout/PageShell";
import { Button } from "@/components/ui/Button";
import { CornerBrackets } from "@/components/ui/CornerBrackets";
import { Input } from "@/components/ui/Input";
import { ApiError } from "@/lib/api/problem";
import { useAuth } from "@/lib/auth/auth-context";

import styles from "./auth.module.css";

function AuthCard() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const { login, register } = useAuth();
  const [tab, setTab] = useState<"login" | "register">(
    searchParams.get("tab") === "register" ? "register" : "login",
  );

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [busy, setBusy] = useState(false);
  const [banner, setBanner] = useState<ApiError | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const resetErrors = () => {
    setBanner(null);
    setFieldErrors({});
  };

  const submitLogin = async (event: FormEvent) => {
    event.preventDefault();
    resetErrors();
    setBusy(true);
    try {
      await login(email, password);
      router.push("/");
    } catch (error) {
      setBanner(
        error instanceof ApiError ? error : new ApiError(0, "Login failed.", null, "Unknown"),
      );
    } finally {
      setBusy(false);
    }
  };

  const submitRegister = async (event: FormEvent) => {
    event.preventDefault();
    resetErrors();

    const problems: Record<string, string> = {};
    if (username.trim().length < 3 || username.trim().length > 32) {
      problems.username = "Username must be 3–32 characters.";
    } else if (!/^[A-Za-z0-9_.-]+$/.test(username.trim())) {
      problems.username = "Only letters, digits, '.', '_' and '-'.";
    }
    if (!displayName.trim()) problems.displayName = "Display name is required.";
    if (password.length < 8) problems.password = "Password must be at least 8 characters.";
    if (Object.keys(problems).length > 0) {
      setFieldErrors(problems);
      return;
    }

    setBusy(true);
    try {
      await register({
        username: username.trim(),
        email: email.trim(),
        displayName: displayName.trim(),
        password,
      });
      router.push("/");
    } catch (error) {
      if (error instanceof ApiError && error.errorType === "Conflict") {
        // e.g. identity.username_taken / identity.email_taken → inline at the field
        const field = error.code?.includes("email") ? "email" : "username";
        setFieldErrors({ [field]: `${error.title} (409 · Conflict)` });
      } else {
        setBanner(
          error instanceof ApiError
            ? error
            : new ApiError(0, "Registration failed.", null, "Unknown"),
        );
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={styles.wrap}>
      <div className={styles.card}>
        <CornerBrackets />
        <div className={styles.logoBlock}>
          <span className={styles.logoMark}>
            <span className={styles.logoDot} />
          </span>
          <div className={styles.logoText}>
            FORUM<span className={styles.logoSlashes}>:{"//"}</span>SIGNAL
          </div>
        </div>

        <div className={styles.tabs}>
          <button
            className={tab === "login" ? styles.tabActive : styles.tab}
            onClick={() => {
              setTab("login");
              resetErrors();
            }}
          >
            LOG IN
          </button>
          <button
            className={tab === "register" ? styles.tabActive : styles.tab}
            onClick={() => {
              setTab("register");
              resetErrors();
            }}
          >
            REGISTER
          </button>
        </div>

        {tab === "login" ? (
          <form className={styles.form} onSubmit={submitLogin}>
            {banner ? (
              <div className={styles.errorBanner} role="alert">
                <span className={styles.errorStatus}>{banner.status || "ERR"}</span>
                <div>
                  <div className={styles.errorTitle}>{banner.title}</div>
                  <div className={styles.errorMeta}>errorType: {banner.errorType}</div>
                </div>
              </div>
            ) : null}
            <Input
              type="email"
              label="Email"
              placeholder="you@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              required
            />
            <Input
              type="password"
              label="Password"
              placeholder="••••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
            <Button size="lg" type="submit" loading={busy}>
              Log in
            </Button>
            <div className={styles.switchNote}>
              No account?{" "}
              <button
                type="button"
                className={styles.switchLink}
                onClick={() => setTab("register")}
              >
                Register
              </button>
            </div>
          </form>
        ) : (
          <form className={styles.form} onSubmit={submitRegister}>
            {banner ? (
              <div className={styles.errorBanner} role="alert">
                <span className={styles.errorStatus}>{banner.status || "ERR"}</span>
                <div>
                  <div className={styles.errorTitle}>{banner.title}</div>
                  <div className={styles.errorMeta}>errorType: {banner.errorType}</div>
                </div>
              </div>
            ) : null}
            <Input
              label="Username"
              placeholder="3–32 chars: letters, digits, . _ -"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              error={fieldErrors.username}
              autoComplete="username"
              required
            />
            <Input
              label="Display name"
              placeholder="Shown next to your posts"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              error={fieldErrors.displayName}
              required
            />
            <Input
              type="email"
              label="Email"
              placeholder="you@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              error={fieldErrors.email}
              autoComplete="email"
              required
            />
            <Input
              type="password"
              label="Password"
              placeholder="Min. 8 characters"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              error={fieldErrors.password}
              autoComplete="new-password"
              required
            />
            <Button size="lg" type="submit" loading={busy}>
              Create account
            </Button>
            <div className={styles.switchNote}>
              Already registered?{" "}
              <button type="button" className={styles.switchLink} onClick={() => setTab("login")}>
                Log in
              </button>
            </div>
          </form>
        )}

        <div className={styles.footer}>
          ACCESS TOKEN 15 MIN · REFRESH COOKIE 14 D · SAMESITE=STRICT
        </div>
      </div>
    </div>
  );
}

export default function AuthPage() {
  return (
    <PageShell wide={false}>
      <Suspense>
        <AuthCard />
      </Suspense>
    </PageShell>
  );
}
