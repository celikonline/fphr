import { describe, it, expect, vi, beforeEach } from "vitest";
import * as workflowService from "./workflow.service";

// Mock apiClient
// vi.mock factories are hoisted above regular top-level statements, so the
// mocked value must be created with vi.hoisted() to avoid a TDZ error.
const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("workflow.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("listInbox", () => {
    it("fetches inbox from correct endpoint", async () => {
      const mockTasks: workflowService.InboxTaskDto[] = [
        {
          taskId: "task-1",
          approvalRequestId: "req-1",
          requestType: "Leave",
          subjectId: "leave-123",
          requesterName: "John Doe",
          stepNumber: 1,
          isFallbackRoleTask: false,
          submittedAt: "2026-09-17T10:00:00Z",
        },
      ];

      mockApiClient.get.mockResolvedValueOnce({ data: mockTasks });

      const result = await workflowService.listInbox();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/workflow/inbox");
      expect(result).toEqual(mockTasks);
    });

    it("returns empty array when no tasks in inbox", async () => {
      mockApiClient.get.mockResolvedValueOnce({ data: [] });

      const result = await workflowService.listInbox();

      expect(result).toEqual([]);
      expect(Array.isArray(result)).toBe(true);
    });

    it("handles InboxTaskDto with all fields populated", async () => {
      const fullTask: workflowService.InboxTaskDto = {
        taskId: "task-123",
        approvalRequestId: "req-456",
        requestType: "Expense",
        subjectId: "expense-789",
        requesterName: "Jane Smith",
        stepNumber: 2,
        isFallbackRoleTask: true,
        submittedAt: "2026-09-16T15:30:00Z",
      };

      mockApiClient.get.mockResolvedValueOnce({ data: [fullTask] });

      const result = await workflowService.listInbox();

      expect(result[0].taskId).toBe("task-123");
      expect(result[0].submittedAt).toBe("2026-09-16T15:30:00Z");
      expect(result[0].requesterName).toBe("Jane Smith");
    });

    it("handles null submittedAt field (API WhenWritingNull)", async () => {
      const taskWithoutSubmittedAt = {
        taskId: "task-456",
        approvalRequestId: "req-789",
        requestType: "Advance",
        subjectId: "advance-101",
        stepNumber: 1,
        isFallbackRoleTask: false,
        submittedAt: null,
      } as workflowService.InboxTaskDto;

      mockApiClient.get.mockResolvedValueOnce({ data: [taskWithoutSubmittedAt] });

      const result = await workflowService.listInbox();

      expect(result[0].submittedAt).toBeNull();
    });

    it("handles missing submittedAt field (WhenWritingNull omission)", async () => {
      const taskWithoutSubmittedAt = {
        taskId: "task-789",
        approvalRequestId: "req-101",
        requestType: "Leave",
        subjectId: "leave-202",
        stepNumber: 1,
        isFallbackRoleTask: false,
        // submittedAt intentionally omitted
      } as workflowService.InboxTaskDto;

      mockApiClient.get.mockResolvedValueOnce({ data: [taskWithoutSubmittedAt] });

      const result = await workflowService.listInbox();

      expect(result[0].submittedAt).toBeUndefined();
    });

    it("handles missing optional requesterName", async () => {
      const taskWithoutRequesterName = {
        taskId: "task-101",
        approvalRequestId: "req-202",
        requestType: "Requisition",
        subjectId: "req-303",
        stepNumber: 1,
        isFallbackRoleTask: false,
        // requesterName omitted
      } as workflowService.InboxTaskDto;

      mockApiClient.get.mockResolvedValueOnce({ data: [taskWithoutRequesterName] });

      const result = await workflowService.listInbox();

      expect(result[0].requesterName).toBeUndefined();
      // Must have required fields
      expect(result[0].subjectId).toBe("req-303");
      expect(result[0].taskId).toBe("task-101");
    });

    it("returns multiple tasks in correct order", async () => {
      const tasks: workflowService.InboxTaskDto[] = [
        {
          taskId: "task-1",
          approvalRequestId: "req-1",
          requestType: "Leave",
          subjectId: "leave-1",
          stepNumber: 1,
          isFallbackRoleTask: false,
        },
        {
          taskId: "task-2",
          approvalRequestId: "req-2",
          requestType: "Expense",
          subjectId: "expense-2",
          stepNumber: 1,
          isFallbackRoleTask: false,
        },
        {
          taskId: "task-3",
          approvalRequestId: "req-3",
          requestType: "Overtime",
          subjectId: "overtime-3",
          stepNumber: 2,
          isFallbackRoleTask: true,
        },
      ];

      mockApiClient.get.mockResolvedValueOnce({ data: tasks });

      const result = await workflowService.listInbox();

      expect(result).toHaveLength(3);
      expect(result.map((t) => t.taskId)).toEqual(["task-1", "task-2", "task-3"]);
    });

    it("preserves subjectId even when submittedAt is missing", async () => {
      const task = {
        taskId: "task-x",
        approvalRequestId: "req-x",
        requestType: "Leave",
        subjectId: "leave-x",
        stepNumber: 1,
        isFallbackRoleTask: false,
        // submittedAt and requesterName omitted
      } as workflowService.InboxTaskDto;

      mockApiClient.get.mockResolvedValueOnce({ data: [task] });

      const result = await workflowService.listInbox();

      expect(result[0].subjectId).toBe("leave-x");
      expect(result[0].submittedAt).toBeUndefined();
    });

    it("handles various requestType values", async () => {
      const requestTypes = ["Leave", "Advance", "Expense", "Overtime", "Requisition"];
      const tasks = requestTypes.map((type, idx) => ({
        taskId: `task-${idx}`,
        approvalRequestId: `req-${idx}`,
        requestType: type,
        subjectId: `subject-${idx}`,
        stepNumber: 1,
        isFallbackRoleTask: false,
      }));

      mockApiClient.get.mockResolvedValueOnce({ data: tasks });

      const result = await workflowService.listInbox();

      expect(result.map((t) => t.requestType)).toEqual(requestTypes);
    });
  });

  describe("error handling", () => {
    it("propagates API error from listInbox", async () => {
      const apiError = new Error("Server error");

      mockApiClient.get.mockRejectedValueOnce(apiError);

      await expect(workflowService.listInbox()).rejects.toBe(apiError);
    });
  });
});
