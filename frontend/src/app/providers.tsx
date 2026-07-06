"use client";

/**
 * Client-side provider stack. Everything below TopNav is client-rendered — the Next
 * server never talks to the .NET API (hard constraint #1); React Query owns all server
 * state, fed by the browser's fetch + the realtime invalidation feed.
 */

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import { ComposeProvider } from "@/components/compose/compose-context";
import { ToastProvider } from "@/components/ui/toast";
import { ApiError } from "@/lib/api/problem";
import { AuthProvider } from "@/lib/auth/auth-context";
import { RealtimeProvider } from "@/lib/realtime/realtime-context";

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 10_000,
            // 4xx problems are deterministic — retrying them only hammers the rate limiter.
            retry: (failureCount, error) => {
              if (error instanceof ApiError && error.status > 0 && error.status < 500) return false;
              return failureCount < 2;
            },
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AuthProvider>
          <RealtimeProvider>
            <ComposeProvider>{children}</ComposeProvider>
          </RealtimeProvider>
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}
