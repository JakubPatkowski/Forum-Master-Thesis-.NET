"use client";

/**
 * Toast system. `useToast().showError(error)` is the standard sink for async ApiErrors —
 * including the empty-body 429, which surfaces as the design's "Slow down a little"
 * warning toast keyed off the status alone.
 */

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

import { ApiError } from "@/lib/api/problem";

import styles from "./toast.module.css";

export type ToastKind = "success" | "error" | "warning" | "info";

interface ToastEntry {
  id: number;
  kind: ToastKind;
  title: string;
  message?: string;
}

interface ToastContextValue {
  show: (kind: ToastKind, title: string, message?: string) => void;
  showError: (error: unknown, fallbackTitle?: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const TOAST_TTL_MS = 5000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastEntry[]>([]);
  const counter = useRef(0);

  const dismiss = useCallback((id: number) => {
    setToasts((list) => list.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (kind: ToastKind, title: string, message?: string) => {
      counter.current += 1;
      const id = counter.current;
      setToasts((list) => [...list, { id, kind, title, message }]);
      setTimeout(() => dismiss(id), TOAST_TTL_MS);
    },
    [dismiss],
  );

  const showError = useCallback(
    (error: unknown, fallbackTitle = "Something went wrong.") => {
      if (error instanceof ApiError) {
        if (error.errorType === "TooManyRequests") {
          show("warning", "Slow down a little", "Too many requests — try again in a moment.");
          return;
        }
        show("error", error.title, error.code ?? undefined);
        return;
      }
      show("error", fallbackTitle);
    },
    [show],
  );

  const value = useMemo(() => ({ show, showError }), [show, showError]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className={styles.viewport} aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`${styles.toast} ${styles[toast.kind]}`}>
            <span className={styles.dot} />
            <div className={styles.text}>
              <div className={styles.title}>{toast.title}</div>
              {toast.message ? <div className={styles.message}>{toast.message}</div> : null}
            </div>
            <button className={styles.close} onClick={() => dismiss(toast.id)} aria-label="Dismiss">
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToast must be used within ToastProvider");
  return context;
}
