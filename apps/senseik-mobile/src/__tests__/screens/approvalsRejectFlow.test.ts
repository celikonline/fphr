import {
  initialRejectFlowState,
  rejectFlowReducer,
  type RejectFlowState,
} from "@/screens/app/approvalsRejectFlow";

const rejecting = (overrides: Partial<Extract<RejectFlowState, { type: "rejecting" }>> = {}) =>
  ({
    type: "rejecting" as const,
    taskId: "task-1",
    reason: "",
    keyboardVisible: false,
    ...overrides,
  });

const deciding = (overrides: Partial<Extract<RejectFlowState, { type: "deciding" }>> = {}) =>
  ({
    type: "deciding" as const,
    taskId: "task-1",
    reason: "",
    ...overrides,
  });

describe("rejectFlowReducer (B19)", () => {
  it("starts idle", () => {
    expect(initialRejectFlowState).toEqual({ type: "idle" });
  });

  it("START_REJECT opens the modal for the given task, keyboard closed, reason empty", () => {
    const result = rejectFlowReducer(initialRejectFlowState, {
      type: "START_REJECT",
      taskId: "task-1",
    });
    expect(result).toEqual(rejecting());
  });

  it("START_REJECT resets even from an unexpected non-idle state", () => {
    const result = rejectFlowReducer(rejecting({ taskId: "task-1", reason: "old" }), {
      type: "START_REJECT",
      taskId: "task-2",
    });
    expect(result).toEqual(rejecting({ taskId: "task-2" }));
  });

  it("SET_REASON updates the reason while rejecting", () => {
    const result = rejectFlowReducer(rejecting(), { type: "SET_REASON", reason: "Bütçe uygun değil" });
    expect(result).toEqual(rejecting({ reason: "Bütçe uygun değil" }));
  });

  it("SET_REASON is ignored outside of rejecting", () => {
    expect(rejectFlowReducer(initialRejectFlowState, { type: "SET_REASON", reason: "x" })).toEqual(
      initialRejectFlowState
    );
    expect(rejectFlowReducer(deciding(), { type: "SET_REASON", reason: "x" })).toEqual(deciding());
  });

  it("KEYBOARD_SHOWN/KEYBOARD_HIDDEN track keyboard visibility while rejecting", () => {
    const shown = rejectFlowReducer(rejecting(), { type: "KEYBOARD_SHOWN" });
    expect(shown).toEqual(rejecting({ keyboardVisible: true }));

    const hidden = rejectFlowReducer(shown, { type: "KEYBOARD_HIDDEN" });
    expect(hidden).toEqual(rejecting({ keyboardVisible: false }));
  });

  it("KEYBOARD_SHOWN/KEYBOARD_HIDDEN are ignored outside of rejecting", () => {
    expect(rejectFlowReducer(initialRejectFlowState, { type: "KEYBOARD_SHOWN" })).toEqual(
      initialRejectFlowState
    );
    expect(rejectFlowReducer(deciding(), { type: "KEYBOARD_HIDDEN" })).toEqual(deciding());
  });

  describe("REQUEST_CLOSE (hardware back button / Modal onRequestClose)", () => {
    it("first back press with the keyboard open only dismisses the keyboard, modal stays open", () => {
      const result = rejectFlowReducer(rejecting({ keyboardVisible: true, reason: "taslak" }), {
        type: "REQUEST_CLOSE",
      });
      expect(result).toEqual(rejecting({ keyboardVisible: false, reason: "taslak" }));
    });

    it("a second back press, once the keyboard is already closed, fully resets to idle", () => {
      const afterFirstPress = rejecting({ keyboardVisible: false, reason: "taslak" });
      const result = rejectFlowReducer(afterFirstPress, { type: "REQUEST_CLOSE" });
      expect(result).toEqual(initialRejectFlowState);
    });

    it("a back press while the keyboard was never open closes the modal on the first press", () => {
      const result = rejectFlowReducer(rejecting({ keyboardVisible: false }), {
        type: "REQUEST_CLOSE",
      });
      expect(result).toEqual(initialRejectFlowState);
    });

    it("is ignored while a decision is in flight - never interrupts an in-flight decideTask call", () => {
      const state = deciding({ reason: "taslak" });
      expect(rejectFlowReducer(state, { type: "REQUEST_CLOSE" })).toBe(state);
    });

    it("is a no-op when already idle", () => {
      expect(rejectFlowReducer(initialRejectFlowState, { type: "REQUEST_CLOSE" })).toEqual(
        initialRejectFlowState
      );
    });
  });

  describe("CANCEL (Vazgeç button)", () => {
    it("fully resets to idle from rejecting", () => {
      expect(rejectFlowReducer(rejecting({ reason: "taslak" }), { type: "CANCEL" })).toEqual(
        initialRejectFlowState
      );
    });

    it("is ignored while deciding (the button is disabled, this guards against it anyway)", () => {
      const state = deciding();
      expect(rejectFlowReducer(state, { type: "CANCEL" })).toBe(state);
    });
  });

  describe("submit lifecycle", () => {
    it("SUBMIT_START moves from rejecting to deciding, carrying the taskId and reason over", () => {
      const result = rejectFlowReducer(rejecting({ reason: "Bütçe uygun değil" }), {
        type: "SUBMIT_START",
      });
      expect(result).toEqual(deciding({ reason: "Bütçe uygun değil" }));
    });

    it("SUBMIT_SUCCESS fully resets to idle - the deciding flag is cleared on the success path", () => {
      expect(rejectFlowReducer(deciding(), { type: "SUBMIT_SUCCESS" })).toEqual(
        initialRejectFlowState
      );
    });

    it("SUBMIT_ERROR returns to rejecting (deciding flag cleared) with the reason preserved for retry", () => {
      const result = rejectFlowReducer(deciding({ reason: "Bütçe uygun değil" }), {
        type: "SUBMIT_ERROR",
      });
      expect(result).toEqual(rejecting({ reason: "Bütçe uygun değil", keyboardVisible: false }));
    });

    it("SUBMIT_START/SUBMIT_SUCCESS/SUBMIT_ERROR are no-ops from the wrong state", () => {
      expect(rejectFlowReducer(initialRejectFlowState, { type: "SUBMIT_START" })).toEqual(
        initialRejectFlowState
      );
      expect(rejectFlowReducer(rejecting(), { type: "SUBMIT_SUCCESS" })).toEqual(rejecting());
      expect(rejectFlowReducer(rejecting(), { type: "SUBMIT_ERROR" })).toEqual(rejecting());
    });
  });
});
