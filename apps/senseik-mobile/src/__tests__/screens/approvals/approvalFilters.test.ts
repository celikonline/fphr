import {
  approvalFilterOptions,
  approvalFilterSelectedText,
  countByFilter,
  filterTasks,
} from "@/screens/app/approvals/approvalFilters";
import type { InboxTask } from "@/services/ApprovalsService";

function task(requestType: string, taskId: string): InboxTask {
  return {
    taskId,
    approvalRequestId: `approval-${taskId}`,
    requestType,
    subjectId: `subject-${taskId}`,
    stepNumber: 1,
    isFallbackRoleTask: false,
  };
}

describe("countByFilter / filterTasks (QA-103: ExpenseClaim must count as 'expense')", () => {
  it("counts 'Tümü' as the sum of the typed buckets for a mixed inbox including 'ExpenseClaim'", () => {
    const tasks = [
      task("Leave", "1"),
      task("Leave", "2"),
      task("Advance", "3"),
      task("Advance", "4"),
      task("ExpenseClaim", "5"),
    ];

    const counts = countByFilter(tasks);

    expect(counts).toEqual({ all: 5, leave: 2, overtime: 0, expense: 1, advance: 2 });
    expect(counts.all).toBe(counts.leave + counts.overtime + counts.expense + counts.advance);
  });

  it("filters 'ExpenseClaim' tasks under the 'expense' bucket", () => {
    const tasks = [task("ExpenseClaim", "1"), task("Advance", "2")];
    expect(filterTasks(tasks, "expense").map((t) => t.taskId)).toEqual(["1"]);
  });

  it("keeps an unmapped request type (Requisition) out of every typed bucket but inside 'all'", () => {
    const tasks = [task("Requisition", "1"), task("Leave", "2")];
    const counts = countByFilter(tasks);
    expect(counts.all).toBe(2);
    expect(counts.leave).toBe(1);
    expect(filterTasks(tasks, "all")).toHaveLength(2);
    expect(filterTasks(tasks, "leave")).toHaveLength(1);
  });
});

describe("countByFilter / filterTasks work on any requestType-shaped list (FG-008 Geçmiş reuse)", () => {
  it("filters an ApprovalHistoryItem-shaped list the same way as InboxTask", () => {
    const historyItems = [
      { taskId: "1", requestType: "Leave", outcome: "approved" as const },
      { taskId: "2", requestType: "Overtime", outcome: "rejected" as const },
    ];

    expect(countByFilter(historyItems)).toEqual({
      all: 2,
      leave: 1,
      overtime: 1,
      expense: 0,
      advance: 0,
    });
    expect(filterTasks(historyItems, "overtime")).toEqual([historyItems[1]]);
  });
});

describe("approvalFilterOptions / approvalFilterSelectedText", () => {
  it("labels the expense option 'Masraf' with its count", () => {
    const counts = countByFilter([task("ExpenseClaim", "1")]);
    const options = approvalFilterOptions(counts);
    expect(options).toContainEqual({ value: "expense", label: "Masraf (1)" });
    expect(approvalFilterSelectedText("expense", counts)).toBe("Masraf (1)");
  });
});
