import { leaveService } from "@/services/LeaveService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("LeaveService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getMyBalances", () => {
    it("fetches the caller's own leave balances", async () => {
      const mockResponse = {
        data: {
          employeeId: "employee-1",
          ruleSetId: "rule-set-1",
          hireDate: "2020-01-01",
          leaveAccrualStartDate: "2020-01-01",
          balances: [
            {
              leaveTypeName: "Yıllık İzin",
              leaveTypeCode: "annual",
              balance: {
                leaveTypeId: "type-1",
                currentPeriodFrom: "2026-01-01",
                currentPeriodTo: "2026-12-31",
                nextAccrualDate: "2027-01-01",
                totalAccrued: 14,
                totalUsed: 2,
                currentBalance: 12,
                availableBalance: 12
              }
            }
          ]
        }
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyBalances();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/balances");
    });
  });

  describe("getMyRequests", () => {
    it("fetches the caller's own leave requests", async () => {
      const mockResponse = { data: [{ id: "request-1", status: "pending" }] };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyRequests();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/requests");
    });
  });

  describe("getMyRequestableLeaveTypes", () => {
    it("fetches the leave types the caller may self-service request", async () => {
      const mockResponse = { data: [{ id: "type-1", code: "annual", name: "Yıllık İzin" }] };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyRequestableLeaveTypes();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/leave-types");
    });
  });

  describe("submitLeaveRequest", () => {
    it("posts a new self-service leave request", async () => {
      const input = { leaveTypeId: "type-1", startDate: "2026-10-01", endDate: "2026-10-03", description: null };
      const mockResponse = { data: { id: "request-2", status: "pending" } };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.submitLeaveRequest(input);

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/requests", input);
    });
  });

  describe("withdrawLeaveRequest", () => {
    it("withdraws a pending leave request by id", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await leaveService.withdrawLeaveRequest("request-1");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/requests/request-1/withdraw");
    });
  });
});
