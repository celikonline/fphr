import {
  canSubmitComment,
  COMMENT_MAX_LENGTH,
  findApprovalRequestId,
  highlightedStepNumber,
  isCommentingAllowed,
  overallStatusLabelKey,
  overallStatusTone,
  REQUEST_KIND_TO_REQUEST_TYPE,
  stepVisualStatus,
} from "@/screens/app/requests/requestTimeline";
import type { MyApprovalRequestSummary } from "@/services/WorkflowService";

describe("REQUEST_KIND_TO_REQUEST_TYPE (FG-009)", () => {
  it("maps every RequestKind to its backend requestType string", () => {
    expect(REQUEST_KIND_TO_REQUEST_TYPE).toEqual({
      leave: "Leave",
      overtime: "Overtime",
      expense: "ExpenseClaim",
      advance: "Advance",
      punchCorrection: "PunchCorrection",
    });
  });
});

describe("findApprovalRequestId (FG-009 kontrolcü notu)", () => {
  const myRequests: MyApprovalRequestSummary[] = [
    {
      approvalRequestId: "approval-leave-1",
      requestType: "Leave",
      subjectId: "subject-1",
      status: "Pending",
      stepCount: 1,
      submittedAt: "2026-09-20T08:00:00Z",
    },
    {
      approvalRequestId: "approval-expense-1",
      requestType: "ExpenseClaim",
      subjectId: "subject-2",
      status: "Approved",
      stepCount: 1,
      submittedAt: "2026-09-18T08:00:00Z",
    },
  ];

  it("finds the approvalRequestId matching both subjectId and the kind's requestType", () => {
    expect(findApprovalRequestId(myRequests, "subject-1", "leave")).toBe("approval-leave-1");
    expect(findApprovalRequestId(myRequests, "subject-2", "expense")).toBe("approval-expense-1");
  });

  it("returns undefined when the subjectId matches but the kind's requestType doesn't (no false positive)", () => {
    expect(findApprovalRequestId(myRequests, "subject-1", "overtime")).toBeUndefined();
  });

  it("returns undefined when there is no matching row at all", () => {
    expect(findApprovalRequestId(myRequests, "subject-unknown", "leave")).toBeUndefined();
  });

  it("returns undefined for an empty list", () => {
    expect(findApprovalRequestId([], "subject-1", "leave")).toBeUndefined();
  });
});

describe("canSubmitComment", () => {
  it("rejects an empty or whitespace-only comment", () => {
    expect(canSubmitComment("")).toBe(false);
    expect(canSubmitComment("   ")).toBe(false);
  });

  it("accepts a trimmed, non-empty comment within the limit", () => {
    expect(canSubmitComment("Onaylandı, teşekkürler")).toBe(true);
  });

  it("rejects a comment longer than the server limit once trimmed", () => {
    expect(canSubmitComment("a".repeat(COMMENT_MAX_LENGTH))).toBe(true);
    expect(canSubmitComment("a".repeat(COMMENT_MAX_LENGTH + 1))).toBe(false);
  });
});

describe("isCommentingAllowed (backend Karar: only Cancelled closes comments)", () => {
  it("allows commenting on a pending/approved/rejected request", () => {
    expect(isCommentingAllowed("Pending")).toBe(true);
    expect(isCommentingAllowed("Approved")).toBe(true);
    expect(isCommentingAllowed("Rejected")).toBe(true);
  });

  it("closes comments only on a cancelled request, case-insensitively", () => {
    expect(isCommentingAllowed("Cancelled")).toBe(false);
    expect(isCommentingAllowed("cancelled")).toBe(false);
  });
});

describe("highlightedStepNumber", () => {
  it("returns currentStepNumber while the request is pending", () => {
    expect(highlightedStepNumber({ status: "Pending", currentStepNumber: 2 })).toBe(2);
  });

  it("returns undefined once the request is no longer pending, even if currentStepNumber is set", () => {
    expect(highlightedStepNumber({ status: "Approved", currentStepNumber: 2 })).toBeUndefined();
    expect(highlightedStepNumber({ status: "Rejected", currentStepNumber: 1 })).toBeUndefined();
  });

  it("returns undefined when pending but currentStepNumber is absent", () => {
    expect(highlightedStepNumber({ status: "Pending", currentStepNumber: null })).toBeUndefined();
  });
});

describe("stepVisualStatus", () => {
  it("shows approved/rejected steps as such regardless of the request's own status", () => {
    expect(stepVisualStatus({ status: "approved" }, { status: "Pending" })).toBe("approved");
    expect(stepVisualStatus({ status: "rejected" }, { status: "Rejected" })).toBe("rejected");
  });

  it("shows a pending step as pending only while the request itself is still pending", () => {
    expect(stepVisualStatus({ status: "pending" }, { status: "Pending" })).toBe("pending");
  });

  it("shows a step that stayed pending on a finished request as notReached (never got its turn)", () => {
    expect(stepVisualStatus({ status: "pending" }, { status: "Rejected" })).toBe("notReached");
    expect(stepVisualStatus({ status: "pending" }, { status: "Cancelled" })).toBe("notReached");
  });
});

describe("overallStatusTone / overallStatusLabelKey", () => {
  it("maps each known status to its tone and leave:status.* label key", () => {
    expect(overallStatusTone("Approved")).toBe("success");
    expect(overallStatusLabelKey("Approved")).toBe("leave:status.approved");

    expect(overallStatusTone("Rejected")).toBe("danger");
    expect(overallStatusLabelKey("Rejected")).toBe("leave:status.rejected");

    expect(overallStatusTone("Cancelled")).toBe("neutral");
    expect(overallStatusLabelKey("Cancelled")).toBe("leave:status.cancelled");

    expect(overallStatusTone("Pending")).toBe("warning");
    expect(overallStatusLabelKey("Pending")).toBe("leave:status.pending");
  });

  it("falls back to pending's tone/label for an unrecognized status (never crashes)", () => {
    expect(overallStatusTone("SomethingNew")).toBe("warning");
    expect(overallStatusLabelKey("SomethingNew")).toBe("leave:status.pending");
  });
});
