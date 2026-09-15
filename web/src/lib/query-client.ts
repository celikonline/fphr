import { QueryClient } from "@tanstack/react-query";

/**
 * Shared React Query client for server-state caching across all Item 20 screens.
 * Sensible defaults for an admin/back-office app: data is not considered
 * instantly stale, retries are limited, and window refocus refetching is off
 * (avoids surprising table resets while an admin is mid-edit).
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
});
