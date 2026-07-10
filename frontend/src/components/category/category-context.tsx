"use client";

/**
 * Global entry point for the category create/edit modal, mirroring the thread composer
 * (compose-context). Any page — the sidebar's "+ New category", the category header's
 * "Edit category" — can open it without owning modal state.
 */

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";

import { CategoryModal } from "@/components/category/CategoryModal";
import type { CategoryResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

interface CategoryModalContextValue {
  /** Opens the create form. Redirects anonymous users to /auth (the backend gates on `create`). */
  openCreateCategory: () => void;
  openEditCategory: (category: CategoryResponse) => void;
}

const CategoryModalContext = createContext<CategoryModalContextValue | null>(null);

type ModalState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "edit"; category: CategoryResponse };

export function CategoryModalProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ModalState>({ mode: "closed" });
  const { isAuthenticated } = useAuth();
  const router = useRouter();

  const openCreateCategory = useCallback(() => {
    if (!isAuthenticated) {
      router.push("/auth");
      return;
    }
    setState({ mode: "create" });
  }, [isAuthenticated, router]);

  const openEditCategory = useCallback((category: CategoryResponse) => {
    setState({ mode: "edit", category });
  }, []);

  const close = useCallback(() => setState({ mode: "closed" }), []);

  const value = useMemo(
    () => ({ openCreateCategory, openEditCategory }),
    [openCreateCategory, openEditCategory],
  );

  return (
    <CategoryModalContext.Provider value={value}>
      {children}
      {state.mode !== "closed" ? (
        <CategoryModal
          mode={state.mode}
          category={state.mode === "edit" ? state.category : undefined}
          onClose={close}
        />
      ) : null}
    </CategoryModalContext.Provider>
  );
}

export function useCategoryModal(): CategoryModalContextValue {
  const context = useContext(CategoryModalContext);
  if (!context) throw new Error("useCategoryModal must be used within CategoryModalProvider");
  return context;
}
