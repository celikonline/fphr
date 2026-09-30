import type { BulkDecisionItemResult } from "@/services/ApprovalsService";

/**
 * FG-008: pure selection state for "Onaylarım"'s toplu seçim mode - kept separate from
 * `approvalsRejectFlow.ts`'s single-task reducer since selecting/deselecting cards has none of that
 * reducer's Android-back/keyboard edge cases, just a plain set of checked task ids.
 */
export type SelectionMode = "browsing" | "selecting";

export interface SelectionState {
  mode: SelectionMode;
  selectedIds: string[];
}

export const initialSelectionState: SelectionState = { mode: "browsing", selectedIds: [] };

/** "Seç" - always starts from an empty selection, regardless of what was selected before. */
export function enterSelectionMode(): SelectionState {
  return { mode: "selecting", selectedIds: [] };
}

/** "İptal" - drops back to the normal card actions (approve/reject per card). */
export function exitSelectionMode(): SelectionState {
  return initialSelectionState;
}

/** A card's checkbox tap - no-op while browsing (the checkbox isn't rendered then). */
export function toggleTaskSelection(state: SelectionState, taskId: string): SelectionState {
  if (state.mode !== "selecting") {
    return state;
  }
  const selectedIds = state.selectedIds.includes(taskId)
    ? state.selectedIds.filter((id) => id !== taskId)
    : [...state.selectedIds, taskId];
  return { ...state, selectedIds };
}

/** "Tümünü seç" - selects every currently visible (filtered) task. */
export function selectAllVisible(state: SelectionState, visibleTaskIds: string[]): SelectionState {
  if (state.mode !== "selecting") {
    return state;
  }
  return { ...state, selectedIds: [...visibleTaskIds] };
}

export interface BulkDecisionReconciliation {
  /** Task ids to remove from the list - decided successfully. */
  succeededTaskIds: string[];
  /** Task ids that stay in the list, still selected, with their (already localized) error. */
  failedTaskIds: string[];
  /** One entry per failed task - the server's localized error message, shown on its card. */
  errorsByTaskId: Record<string, string>;
}

/**
 * FG-008: partial success reconciliation - a bulk decide answers one result per requested task, in
 * request order (`DecideBulkResponse`). Succeeded tasks are dropped from the list; failed ones stay,
 * still selected, with their error shown (spec: "kısmi başarısızlıkta başarılılar listeden düşer,
 * başarısızlar hata nedeniyle işaretli kalır").
 */
export function reconcileBulkResults(
  results: BulkDecisionItemResult[]
): BulkDecisionReconciliation {
  const succeededTaskIds: string[] = [];
  const failedTaskIds: string[] = [];
  const errorsByTaskId: Record<string, string> = {};

  for (const result of results) {
    if (result.succeeded) {
      succeededTaskIds.push(result.taskId);
    } else {
      failedTaskIds.push(result.taskId);
      if (result.errorMessage) {
        errorsByTaskId[result.taskId] = result.errorMessage;
      }
    }
  }

  return { succeededTaskIds, failedTaskIds, errorsByTaskId };
}
