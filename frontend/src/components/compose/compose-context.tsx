"use client";

/**
 * Global entry point for the thread composer modal (design: Compose.dc.html renders as
 * a modal over the ghosted forum, not a route). Any page can call openCreate/openEdit.
 */

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";

import { ComposeThreadModal } from "@/components/compose/ComposeThreadModal";
import type { ThreadDetailResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

interface ComposeContextValue {
  /** Opens the create composer (optionally pre-selecting a category). Redirects anon users to /auth. */
  openCreate: (categoryId?: string) => void;
  openEdit: (thread: ThreadDetailResponse) => void;
}

const ComposeContext = createContext<ComposeContextValue | null>(null);

type ComposeState =
  | { mode: "closed" }
  | { mode: "create"; categoryId?: string }
  | { mode: "edit"; thread: ThreadDetailResponse };

export function ComposeProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ComposeState>({ mode: "closed" });
  const { isAuthenticated } = useAuth();
  const router = useRouter();

  const openCreate = useCallback(
    (categoryId?: string) => {
      if (!isAuthenticated) {
        router.push("/auth");
        return;
      }
      setState({ mode: "create", categoryId });
    },
    [isAuthenticated, router],
  );

  const openEdit = useCallback((thread: ThreadDetailResponse) => {
    setState({ mode: "edit", thread });
  }, []);

  const close = useCallback(() => setState({ mode: "closed" }), []);

  const value = useMemo(() => ({ openCreate, openEdit }), [openCreate, openEdit]);

  return (
    <ComposeContext.Provider value={value}>
      {children}
      {state.mode !== "closed" ? (
        <ComposeThreadModal
          mode={state.mode}
          initialCategoryId={state.mode === "create" ? state.categoryId : undefined}
          thread={state.mode === "edit" ? state.thread : undefined}
          onClose={close}
        />
      ) : null}
    </ComposeContext.Provider>
  );
}

export function useCompose(): ComposeContextValue {
  const context = useContext(ComposeContext);
  if (!context) throw new Error("useCompose must be used within ComposeProvider");
  return context;
}
