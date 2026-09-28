import {
  enterSelectionMode,
  exitSelectionMode,
  initialSelectionState,
  reconcileBulkResults,
  selectAllVisible,
  toggleTaskSelection,
} from "@/screens/app/approvals/approvalSelection";

describe("initialSelectionState", () => {
  it("starts in browsing mode with nothing selected", () => {
    expect(initialSelectionState).toEqual({ mode: "browsing", selectedIds: [] });
  });
});

describe("enterSelectionMode / exitSelectionMode", () => {
  it("enters selecting mode with an empty selection", () => {
    expect(enterSelectionMode()).toEqual({ mode: "selecting", selectedIds: [] });
  });

  it("exits back to browsing mode, dropping any selection", () => {
    expect(exitSelectionMode()).toEqual({ mode: "browsing", selectedIds: [] });
  });
});

describe("toggleTaskSelection", () => {
  it("does nothing while browsing", () => {
    expect(toggleTaskSelection(initialSelectionState, "task-1")).toBe(initialSelectionState);
  });

  it("selects an unselected task while selecting", () => {
    const state = enterSelectionMode();
    expect(toggleTaskSelection(state, "task-1")).toEqual({
      mode: "selecting",
      selectedIds: ["task-1"],
    });
  });

  it("deselects an already-selected task", () => {
    const state = { mode: "selecting" as const, selectedIds: ["task-1", "task-2"] };
    expect(toggleTaskSelection(state, "task-1")).toEqual({
      mode: "selecting",
      selectedIds: ["task-2"],
    });
  });
});

describe("selectAllVisible", () => {
  it("does nothing while browsing", () => {
    expect(selectAllVisible(initialSelectionState, ["task-1"])).toBe(initialSelectionState);
  });

  it("selects every visible (filtered) task id", () => {
    const state = enterSelectionMode();
    expect(selectAllVisible(state, ["task-1", "task-2"])).toEqual({
      mode: "selecting",
      selectedIds: ["task-1", "task-2"],
    });
  });

  it("replaces a previous selection rather than appending to it", () => {
    const state = { mode: "selecting" as const, selectedIds: ["task-9"] };
    expect(selectAllVisible(state, ["task-1"])).toEqual({
      mode: "selecting",
      selectedIds: ["task-1"],
    });
  });
});

describe("reconcileBulkResults (FG-008 partial success)", () => {
  it("splits succeeded and failed task ids and keeps each failed task's error message", () => {
    const result = reconcileBulkResults([
      { taskId: "task-1", succeeded: true, errorCode: null, errorMessage: null },
      {
        taskId: "task-2",
        succeeded: false,
        errorCode: "workflow.assignee_mismatch",
        errorMessage: "Bu görev size atanmamış.",
      },
    ]);

    expect(result).toEqual({
      succeededTaskIds: ["task-1"],
      failedTaskIds: ["task-2"],
      errorsByTaskId: { "task-2": "Bu görev size atanmamış." },
    });
  });

  it("returns everything succeeded when every task decided cleanly", () => {
    const result = reconcileBulkResults([
      { taskId: "task-1", succeeded: true, errorCode: null, errorMessage: null },
      { taskId: "task-2", succeeded: true, errorCode: null, errorMessage: null },
    ]);

    expect(result).toEqual({
      succeededTaskIds: ["task-1", "task-2"],
      failedTaskIds: [],
      errorsByTaskId: {},
    });
  });

  it("omits a failed task from errorsByTaskId when the server sent no message", () => {
    const result = reconcileBulkResults([
      {
        taskId: "task-1",
        succeeded: false,
        errorCode: "workflow.assignee_mismatch",
        errorMessage: null,
      },
    ]);

    expect(result.failedTaskIds).toEqual(["task-1"]);
    expect(result.errorsByTaskId).toEqual({});
  });
});
