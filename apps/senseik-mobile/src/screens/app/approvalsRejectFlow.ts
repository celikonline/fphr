/**
 * B19: pure state machine for the "Reddet" modal in `ApprovalsScreen`.
 *
 * Root cause of the bug this replaces: the screen used two independent `useState`s
 * (`rejectingTaskId`, `rejectReason`) plus a shared `decidingTaskId`, and the Modal's
 * `onRequestClose` (fired by Android's hardware back button) always called the same
 * `cancelReject()` regardless of whether a decision request was in flight or a keyboard
 * was open. On Android, a `<Modal>`'s native Dialog intercepts the hardware back button
 * itself and calls `onRequestClose` even while a focused `TextInput`'s keyboard is showing
 * (see `react-native/Libraries/Modal/Modal.js` - `onRequestClose` is wired straight to the
 * native dialog's back handling, it has no keyboard-awareness), so one back press closed
 * the keyboard AND the modal together, and (if a decision was mid-flight) could reset the
 * reject state while `decideTask` was still running.
 *
 * Collapsing all of that into one reducer with explicit transitions makes every exit path
 * (submit success, submit error, cancel, hardware back) go through the same code, and makes
 * "ignore back press while deciding" and "first back press only closes the keyboard" simple,
 * testable transitions instead of ad-hoc `if`s spread across event handlers.
 */
export type RejectFlowState =
  | { type: "idle" }
  | { type: "rejecting"; taskId: string; reason: string; keyboardVisible: boolean }
  | { type: "deciding"; taskId: string; reason: string };

export type RejectFlowAction =
  | { type: "START_REJECT"; taskId: string }
  | { type: "SET_REASON"; reason: string }
  | { type: "KEYBOARD_SHOWN" }
  | { type: "KEYBOARD_HIDDEN" }
  | { type: "REQUEST_CLOSE" }
  | { type: "CANCEL" }
  | { type: "SUBMIT_START" }
  | { type: "SUBMIT_SUCCESS" }
  | { type: "SUBMIT_ERROR" };

export const initialRejectFlowState: RejectFlowState = { type: "idle" };

export function rejectFlowReducer(
  state: RejectFlowState,
  action: RejectFlowAction
): RejectFlowState {
  switch (action.type) {
    case "START_REJECT":
      // Always allowed, from any state - "Reddet" is only tappable while idle in the UI
      // (the other tasks' buttons are disabled while one is deciding), but a fresh reject
      // always starts from a clean slate regardless of what the previous state was.
      return { type: "rejecting", taskId: action.taskId, reason: "", keyboardVisible: false };

    case "SET_REASON":
      return state.type === "rejecting" ? { ...state, reason: action.reason } : state;

    case "KEYBOARD_SHOWN":
      return state.type === "rejecting" ? { ...state, keyboardVisible: true } : state;

    case "KEYBOARD_HIDDEN":
      return state.type === "rejecting" ? { ...state, keyboardVisible: false } : state;

    case "REQUEST_CLOSE":
      // Hardware back button (or Modal's own `onRequestClose`). A decision request is never
      // interrupted by a back press - ignore it entirely until SUBMIT_SUCCESS/SUBMIT_ERROR.
      if (state.type === "deciding") {
        return state;
      }
      if (state.type === "rejecting" && state.keyboardVisible) {
        // First back press with the keyboard open: only close the keyboard, keep the modal.
        return { ...state, keyboardVisible: false };
      }
      // Keyboard already closed (or wasn't open): second back press actually closes the modal.
      return initialRejectFlowState;

    case "CANCEL":
      // "Vazgeç" button - disabled while deciding, so this only ever fires from "rejecting".
      return state.type === "rejecting" ? initialRejectFlowState : state;

    case "SUBMIT_START":
      return state.type === "rejecting"
        ? { type: "deciding", taskId: state.taskId, reason: state.reason }
        : state;

    case "SUBMIT_SUCCESS":
      return state.type === "deciding" ? initialRejectFlowState : state;

    case "SUBMIT_ERROR":
      // Keep the modal open with the typed reason so the user can retry, same as before B19.
      return state.type === "deciding"
        ? { type: "rejecting", taskId: state.taskId, reason: state.reason, keyboardVisible: false }
        : state;

    default:
      return state;
  }
}
