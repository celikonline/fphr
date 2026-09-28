import { approvalsService } from "@/services/ApprovalsService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("ApprovalsService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getInbox", () => {
    it("fetches the caller's pending approval tasks", async () => {
      const mockResponse = {
        data: [
          {
            taskId: "task-1",
            approvalRequestId: "request-1",
            requestType: "Leave",
            subjectId: "subject-1",
            requesterName: "Ayşe Yılmaz",
            stepNumber: 1,
            isFallbackRoleTask: false,
            submittedAt: "2026-09-15T08:30:00Z",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await approvalsService.getInbox();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/workflow/inbox");
    });
  });

  describe("decideTask", () => {
    it("approves a task", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await approvalsService.decideTask("task-1", { outcome: "approved" });

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/workflow/inbox/task-1/decide", {
        outcome: "approved",
      });
    });

    it("rejects a task with a comment (rejection reason)", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await approvalsService.decideTask("task-1", {
        outcome: "rejected",
        comment: "Bütçe uygun değil",
      });

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/workflow/inbox/task-1/decide", {
        outcome: "rejected",
        comment: "Bütçe uygun değil",
      });
    });
  });

  describe("getHistory (FG-008)", () => {
    it("fetches the caller's past decisions with the given filters", async () => {
      const mockResponse = {
        data: {
          items: [
            {
              taskId: "task-1",
              approvalRequestId: "request-1",
              requestType: "Leave",
              subjectId: "subject-1",
              requesterEmployeeId: "employee-1",
              requesterName: "Ayşe Yılmaz",
              stepNumber: 1,
              outcome: "approved",
              decidedAt: "2026-09-20T08:30:00Z",
              requestStatus: "approved",
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await approvalsService.getHistory({ page: 1, pageSize: 20, type: "Leave" });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/workflow/inbox/history", {
        params: { page: 1, pageSize: 20, type: "Leave" },
      });
    });

    it("defaults a missing items array to empty (same convention as MyNotificationsPage)", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({
        data: { items: null, page: 1, pageSize: 20, totalCount: 0 },
      });

      const result = await approvalsService.getHistory();

      expect(result.items).toEqual([]);
    });
  });

  describe("decideBulk (FG-008)", () => {
    it("posts the task ids with one outcome and returns the per-task results", async () => {
      const results = [
        { taskId: "task-1", succeeded: true, errorCode: null, errorMessage: null },
        {
          taskId: "task-2",
          succeeded: false,
          errorCode: "workflow.assignee_mismatch",
          errorMessage: "Bu görev size atanmamış.",
        },
      ];
      (apiClient.post as jest.Mock).mockResolvedValue({ data: { results } });

      const result = await approvalsService.decideBulk({
        taskIds: ["task-1", "task-2"],
        outcome: "approved",
      });

      expect(result).toEqual(results);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/workflow/inbox/decide-bulk", {
        taskIds: ["task-1", "task-2"],
        outcome: "approved",
      });
    });

    it("rejects several tasks with one shared comment", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { results: [{ taskId: "task-1", succeeded: true }] },
      });

      await approvalsService.decideBulk({
        taskIds: ["task-1"],
        outcome: "rejected",
        comment: "Bütçe uygun değil",
      });

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/workflow/inbox/decide-bulk", {
        taskIds: ["task-1"],
        outcome: "rejected",
        comment: "Bütçe uygun değil",
      });
    });
  });

  describe("error mapping", () => {
    it("propagates the API error when fetching the inbox fails", async () => {
      const apiError = {
        response: { status: 403, data: { error: { message: "Yetkisiz erişim" } } },
      };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(approvalsService.getInbox()).rejects.toEqual(apiError);
    });

    it("propagates the API error when a decision fails (e.g. task already decided)", async () => {
      const apiError = {
        response: { status: 409, data: { error: { message: "Görev zaten karara bağlanmış" } } },
      };
      (apiClient.post as jest.Mock).mockRejectedValue(apiError);

      await expect(approvalsService.decideTask("task-1", { outcome: "approved" })).rejects.toEqual(
        apiError
      );
    });
  });
});
