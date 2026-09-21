import type { QueryClient } from "@tanstack/react-query";

/**
 * O-14: the leave/overtime request status is flipped by an integration event that the workflow outbox publishes a few
 * seconds after the decision command returns, so the first refetch right after approve/reject can still see
 * `pending`. Refetch immediately (cheap, covers the fast path) and again shortly after, once the outbox has caught up.
 */
export const DECISION_REFRESH_DELAYS_MS = [2500, 6000] as const;

const DECISION_QUERY_ROOTS = [["workflow"], ["leave"]] as const;

function invalidateDecisionQueries(queryClient: QueryClient): void {
  for (const queryKey of DECISION_QUERY_ROOTS) {
    void queryClient.invalidateQueries({ queryKey });
  }
}

/** Invalidates the workflow inbox and leave/overtime queries now, then again after each delay. */
export function refreshAfterDecision(queryClient: QueryClient, delaysMs: readonly number[] = DECISION_REFRESH_DELAYS_MS): void {
  invalidateDecisionQueries(queryClient);
  for (const delay of delaysMs) {
    setTimeout(() => invalidateDecisionQueries(queryClient), delay);
  }
}
