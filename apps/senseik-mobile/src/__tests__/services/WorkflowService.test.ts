import { workflowService } from "@/services/WorkflowService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("WorkflowService (FG-009)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getMyRequests", () => {
    it("fetches the caller's own submitted requests", async () => {
      const mockResponse = {
        data: [
          {
            approvalRequestId: "approval-1",
            requestType: "Leave",
            subjectId: "leave-1",
            status: "Pending",
            stepCount: 2,
            currentStepNumber: 1,
            submittedAt: "2026-09-20T08:00:00Z",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await workflowService.getMyRequests();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/workflow/my-requests");
    });

    it("defaults a missing list to an empty array", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: null });

      expect(await workflowService.getMyRequests()).toEqual([]);
    });
  });

  describe("getStatus", () => {
    it("fetches one approval request's status and step timeline", async () => {
      const mockResponse = {
        data: {
          approvalRequestId: "approval-1",
          requestType: "Leave",
          subjectId: "leave-1",
          requesterEmployeeId: "employee-1",
          status: "Pending",
          stepCount: 2,
          currentStepNumber: 1,
          submittedAt: "2026-09-20T08:00:00Z",
          steps: [
            {
              stepNumber: 1,
              status: "pending",
              approverEmployeeId: "employee-2",
              approverName: "Ayşe Yılmaz",
              isFallbackRole: false,
            },
          ],
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await workflowService.getStatus("approval-1");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/workflow/requests/approval-1/status");
    });
  });

  describe("getComments", () => {
    it("fetches the request's comments, oldest first", async () => {
      const mockResponse = {
        data: [
          {
            id: "comment-1",
            authorEmployeeId: "employee-1",
            authorName: "Ayşe Yılmaz",
            text: "Ne zaman onaylanır?",
            createdAt: "2026-09-20T09:00:00Z",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await workflowService.getComments("approval-1");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/workflow/requests/approval-1/comments");
    });

    it("defaults a missing list to an empty array", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: null });

      expect(await workflowService.getComments("approval-1")).toEqual([]);
    });
  });

  describe("addComment", () => {
    it("posts the comment text and returns the new comment's id", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: { id: "comment-1" } });

      const result = await workflowService.addComment("approval-1", "Onaylandı, teşekkürler");

      expect(result).toEqual({ id: "comment-1" });
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/workflow/requests/approval-1/comments", {
        text: "Onaylandı, teşekkürler",
      });
    });

    it("propagates the API error (e.g. 400 workflow.comment.request_closed)", async () => {
      const apiError = {
        response: { status: 400, data: { code: "workflow.comment.request_closed" } },
      };
      (apiClient.post as jest.Mock).mockRejectedValue(apiError);

      await expect(workflowService.addComment("approval-1", "Merhaba")).rejects.toEqual(apiError);
    });
  });
});
