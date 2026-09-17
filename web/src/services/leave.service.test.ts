import { describe, it, expect, vi, beforeEach } from "vitest";
import * as leaveService from "./leave.service";
import type {
  EmployeeLeaveBalances,
  LeaveBalance,
  LeaveRequest,
  LeaveRequestStatus,
} from "./leave.service";

// Mock @senseik/api-client
const mockApiClient = {
  get: vi.fn(),
  post: vi.fn(),
  patch: vi.fn(),
};

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("leaveService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("path selection", () => {
    it("uses /me endpoint when no employeeId provided", async () => {
      const mockBalances: EmployeeLeaveBalances = {
        employeeId: "current-user",
        ruleSetId: "default",
        hireDate: "2020-01-01",
        leaveAccrualStartDate: "2020-01-01",
        balances: [],
      };
      mockApiClient.get.mockResolvedValue({ data: mockBalances });

      await leaveService.getMyLeaveBalances();

      expect(mockApiClient.get).toHaveBeenCalledWith(
        "/v1/leave/me/balances"
      );
    });

    it("uses /employees/{id} endpoint when employeeId provided", async () => {
      const employeeId = "emp-123";
      const mockBalances: EmployeeLeaveBalances = {
        employeeId,
        ruleSetId: "default",
        hireDate: "2020-01-01",
        leaveAccrualStartDate: "2020-01-01",
        balances: [],
      };
      mockApiClient.get.mockResolvedValue({ data: mockBalances });

      await leaveService.getLeaveBalances(employeeId);

      expect(mockApiClient.get).toHaveBeenCalledWith(
        `/v1/leave/employees/${employeeId}/balances`
      );
    });

    it("uses /me for requests endpoint without employeeId", async () => {
      mockApiClient.get.mockResolvedValue({ data: [] });

      await leaveService.getMyLeaveRequests();

      expect(mockApiClient.get).toHaveBeenCalledWith(
        "/v1/leave/me/requests"
      );
    });

    it("uses /employees/{id} for requests endpoint with employeeId", async () => {
      const employeeId = "emp-456";
      mockApiClient.get.mockResolvedValue({ data: [] });

      await leaveService.getLeaveRequests(employeeId);

      expect(mockApiClient.get).toHaveBeenCalledWith(
        `/v1/leave/employees/${employeeId}/requests`
      );
    });
  });

  describe("date serialization (no timezone shift)", () => {
    it("sends DateOnly fields as YYYY-MM-DD string without timezone conversion", async () => {
      const employeeId = "emp-789";
      // Simulate a request with dates near midnight UTC
      // If timezone shift occurs, a date like 2026-01-01 might serialize incorrectly
      const requestInput = {
        leaveTypeId: "type-1",
        startDate: "2026-01-01", // YYYY-MM-DD format (DateOnly equivalent)
        endDate: "2026-01-03",
        description: "Test leave",
      };

      mockApiClient.post.mockResolvedValue({
        data: { id: "req-1", status: "pending" as const },
      });

      await leaveService.submitLeaveRequestOnBehalf(employeeId, requestInput);

      // Verify that the dates are passed as-is, not converted via toISOString()
      const callArgs = mockApiClient.post.mock.calls[0];
      expect(callArgs[1]).toMatchObject({
        startDate: "2026-01-01",
        endDate: "2026-01-03",
      });
      // Ensure toISOString() conversion did not occur
      // (which would produce "2025-12-31T21:00:00.000Z" in UTC+3)
      expect(callArgs[1].startDate).not.toContain("T");
    });

    it("receives and preserves YYYY-MM-DD dates from API response", async () => {
      const mockRequest: LeaveRequest = {
        id: "req-1",
        employeeId: "emp-1",
        leaveTypeId: "type-1",
        leaveTypeCode: "annual",
        startDate: "2026-03-15",
        endDate: "2026-03-17",
        calculatedDays: 3,
        status: "pending",
        source: "selfService",
      };

      mockApiClient.get.mockResolvedValue({ data: [mockRequest] });

      const result = await leaveService.getMyLeaveRequests();

      expect(result[0].startDate).toBe("2026-03-15");
      expect(result[0].endDate).toBe("2026-03-17");
      // Verify no timezone-based date shift occurred
      expect(result[0].startDate).not.toContain("Z");
    });
  });

  describe("unknown leave type handling", () => {
    it("does not crash on unknown leaveTypeCode in balance list", async () => {
      const mockBalances: EmployeeLeaveBalances = {
        employeeId: "emp-1",
        ruleSetId: "default",
        hireDate: "2020-01-01",
        leaveAccrualStartDate: "2020-01-01",
        balances: [
          {
            leaveTypeName: "Annual",
            leaveTypeCode: "annual",
            balance: {
              leaveTypeId: "type-1",
              currentPeriodFrom: "2026-01-01",
              currentPeriodTo: "2026-12-31",
              nextAccrualDate: "2027-01-01",
              totalAccrued: 20,
              totalUsed: 5,
              extraGrants: 0,
              adjustments: 0,
              carriedOverIntoPeriod: 0,
              lastPeriodAccrued: 0,
              currentPeriodUsed: 5,
              futureApprovedInPeriod: 0,
              nextPeriodApproved: 0,
              currentBalance: 15,
              availableBalance: 15,
            },
          },
          {
            leaveTypeName: "Future Type",
            leaveTypeCode: "futureType" as any, // Simulate new API type not in our enum
            balance: {
              leaveTypeId: "type-2",
              currentPeriodFrom: "2026-01-01",
              currentPeriodTo: "2026-12-31",
              nextAccrualDate: "2027-01-01",
              totalAccrued: 10,
              totalUsed: 0,
              extraGrants: 0,
              adjustments: 0,
              carriedOverIntoPeriod: 0,
              lastPeriodAccrued: 0,
              currentPeriodUsed: 0,
              futureApprovedInPeriod: 0,
              nextPeriodApproved: 0,
              currentBalance: 10,
              availableBalance: 10,
            },
          },
        ],
      };

      mockApiClient.get.mockResolvedValue({ data: mockBalances });

      // Should not throw
      const result = await leaveService.getMyLeaveBalances();

      expect(result.balances).toHaveLength(2);
      expect(result.balances[1].leaveTypeCode).toBe("futureType");
      // Verify the UI can safely iterate/display unknown types
      expect(result.balances.every((b) => b.leaveTypeName)).toBe(true);
    });

    it("handles unknown leaveTypeCode in leave request", async () => {
      const mockRequest: LeaveRequest & { leaveTypeCode: unknown } = {
        id: "req-1",
        employeeId: "emp-1",
        leaveTypeId: "type-1",
        leaveTypeCode: "customNewType" as any,
        startDate: "2026-03-15",
        endDate: "2026-03-17",
        calculatedDays: 3,
        status: "pending",
        source: "selfService",
      };

      mockApiClient.get.mockResolvedValue({ data: [mockRequest] });

      // Should not throw during deserialization
      const result = await leaveService.getMyLeaveRequests();

      expect(result).toHaveLength(1);
      expect((result[0] as any).leaveTypeCode).toBe("customNewType");
    });
  });

  describe("null field handling (API omits nulls via WhenWritingNull)", () => {
    it("handles missing optional fields gracefully", async () => {
      const mockBalanceWithoutOptionals: any = {
        leaveTypeName: "Sick Leave",
        leaveTypeCode: "sick",
        balance: {
          leaveTypeId: "type-3",
          currentPeriodFrom: "2026-01-01",
          currentPeriodTo: "2026-12-31",
          nextAccrualDate: "2027-01-01",
          totalAccrued: 10,
          totalUsed: 2,
          extraGrants: 0,
          adjustments: 0,
          carriedOverIntoPeriod: 0,
          lastPeriodAccrued: 0,
          currentPeriodUsed: 2,
          futureApprovedInPeriod: 0,
          nextPeriodApproved: 0,
          currentBalance: 8,
          availableBalance: 8,
          // negativeBalanceLimit is omitted (null)
        },
      };

      const mockBalances: EmployeeLeaveBalances = {
        employeeId: "emp-1",
        ruleSetId: "default",
        hireDate: "2020-01-01",
        leaveAccrualStartDate: "2020-01-01",
        balances: [mockBalanceWithoutOptionals],
      };

      mockApiClient.get.mockResolvedValue({ data: mockBalances });

      const result = await leaveService.getMyLeaveBalances();

      // UI should handle undefined as null
      expect(result.balances[0].balance.negativeBalanceLimit).toBeUndefined();
      // Accessing it should not crash
      const limit = result.balances[0].balance.negativeBalanceLimit ?? 0;
      expect(limit).toBe(0);
    });
  });
});
