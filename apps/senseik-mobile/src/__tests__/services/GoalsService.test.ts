import { goalsService } from "@/services/GoalsService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("GoalsService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getMyGoals", () => {
    it("fetches the caller's own goals", async () => {
      const mockResponse = {
        data: [
          {
            id: "goal-1",
            employeeId: "employee-1",
            title: "Yıl sonu satış hedefi",
            description: "Q4 hedeflerini tamamla",
            targetDate: "2026-12-31",
            progress: 40,
            status: "inProgress",
            reviewCycleId: null,
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await goalsService.getMyGoals();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/performance/goals/me");
    });
  });

  describe("updateMyGoalProgress", () => {
    it("posts the caller's own goal progress update", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await goalsService.updateMyGoalProgress("goal-1", 75);

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/performance/goals/goal-1/progress/me", {
        progress: 75,
      });
    });
  });

  describe("error mapping", () => {
    it("propagates the API error when fetching goals fails (e.g. no employee record)", async () => {
      const apiError = {
        response: { status: 404, data: { error: { message: "Çalışan bulunamadı" } } },
      };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(goalsService.getMyGoals()).rejects.toEqual(apiError);
    });

    it("propagates the API error when a progress update is rejected (e.g. closed review cycle)", async () => {
      const apiError = { response: { status: 409, data: { error: { message: "Dönem kapalı" } } } };
      (apiClient.post as jest.Mock).mockRejectedValue(apiError);

      await expect(goalsService.updateMyGoalProgress("goal-1", 50)).rejects.toEqual(apiError);
    });
  });
});
