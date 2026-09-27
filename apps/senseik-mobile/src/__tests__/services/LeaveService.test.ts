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
                availableBalance: 12,
              },
            },
          ],
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyBalances();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/balances");
    });

    it("resolves raw seeded leave-type keys in balances to Turkish names", async () => {
      const mockResponse = {
        data: {
          employeeId: "employee-1",
          ruleSetId: "rule-set-1",
          hireDate: "2020-01-01",
          leaveAccrualStartDate: "2020-01-01",
          balances: [
            {
              leaveTypeName: "leave.type.sick",
              leaveTypeCode: "sick",
              balance: { availableBalance: 5 },
            },
          ],
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyBalances();

      expect(result.balances[0].leaveTypeName).toBe("Hastalık izni");
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

    it("resolves raw seeded leave-type keys to Turkish names (senseik-039: the API no longer localizes them)", async () => {
      const mockResponse = {
        data: [
          { id: "type-1", code: "annual", name: "leave.type.annual" },
          { id: "type-2", code: "custom", name: "Özel izin" },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyRequestableLeaveTypes();

      expect(result.map((type) => type.name)).toEqual(["Yıllık izin", "Özel izin"]);
    });

    it("passes requiresSubstitute through unchanged (B-mobil-005: drives the vekil picker)", async () => {
      const mockResponse = {
        data: [
          {
            id: "type-1",
            code: "annual",
            name: "Yıllık İzin",
            unit: "fullDay",
            requiresSubstitute: true,
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyRequestableLeaveTypes();

      expect(result[0].requiresSubstitute).toBe(true);
    });
  });

  describe("submitLeaveRequest", () => {
    it("posts a new self-service leave request", async () => {
      const input = {
        leaveTypeId: "type-1",
        startDate: "2026-10-01",
        endDate: "2026-10-03",
        description: null,
      };
      const mockResponse = { data: { id: "request-2", status: "pending" } };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.submitLeaveRequest(input);

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/requests", input);
    });

    it("forwards halfDayPeriod for a half-day request (D2, same start/end date)", async () => {
      const input = {
        leaveTypeId: "type-1",
        startDate: "2026-10-01",
        endDate: "2026-10-01",
        description: null,
        halfDayPeriod: "morning" as const,
      };
      const mockResponse = { data: { id: "request-3", status: "pending" } };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      await leaveService.submitLeaveRequest(input);

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/requests", input);
    });

    it("forwards startTime/endTime with the same start/end date for an hourly request (A: saatlik izin)", async () => {
      const input = {
        leaveTypeId: "type-1",
        startDate: "2026-10-01",
        endDate: "2026-10-01",
        description: null,
        startTime: "09:00:00",
        endTime: "11:30:00",
      };
      const mockResponse = { data: { id: "request-4", status: "pending" } };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      await leaveService.submitLeaveRequest(input);

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/requests", input);
    });

    it("forwards substituteEmployeeId for a requiresSubstitute request (B-mobil-005)", async () => {
      const input = {
        leaveTypeId: "type-1",
        startDate: "2026-10-01",
        endDate: "2026-10-03",
        description: null,
        substituteEmployeeId: "employee-2",
      };
      const mockResponse = { data: { id: "request-5", status: "pending" } };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      await leaveService.submitLeaveRequest(input);

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

  describe("getEmployeeLeaveRequest (A1: onaycı - attachments)", () => {
    it("fetches the manager-scoped request detail by employeeId and requestId", async () => {
      const mockResponse = {
        data: {
          attachments: [
            { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
          ],
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getEmployeeLeaveRequest("employee-1", "request-1");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith(
        "/api/v1/leave/employees/employee-1/requests/request-1"
      );
    });
  });

  describe("getMyLeaveRequest (senseik-mobile-011: self-service - lazy attachments)", () => {
    it("fetches the caller's own request detail by requestId", async () => {
      const mockResponse = {
        data: {
          attachments: [
            { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
          ],
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyLeaveRequest("request-1");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/requests/request-1");
    });
  });

  describe("getEmployeeAttachmentDownloadUrl (senseik-mobile-011: onaycı/İK)", () => {
    it("fetches the manager-scoped presigned download URL", async () => {
      const mockResponse = {
        data: {
          url: "https://storage.example/documents/doc-1?sig=abc",
          expiresAt: "2026-09-27T09:15:00Z",
          fileName: "rapor.pdf",
          contentType: "application/pdf",
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getEmployeeAttachmentDownloadUrl(
        "employee-1",
        "request-1",
        "doc-1"
      );

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith(
        "/api/v1/leave/employees/employee-1/requests/request-1/attachments/doc-1/download-url"
      );
    });
  });

  describe("getMyAttachmentDownloadUrl (senseik-mobile-011: talep sahibi)", () => {
    it("fetches the self-service presigned download URL", async () => {
      const mockResponse = {
        data: {
          url: "https://storage.example/documents/doc-1?sig=xyz",
          expiresAt: "2026-09-27T09:15:00Z",
          fileName: "rapor.pdf",
          contentType: "application/pdf",
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyAttachmentDownloadUrl("request-1", "doc-1");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith(
        "/api/v1/leave/me/requests/request-1/attachments/doc-1/download-url"
      );
    });

    it("propagates a 403/404 (out of scope, request/attachment/document not found) instead of swallowing it", async () => {
      const apiError = {
        response: { status: 404, data: { error: { code: "leave.errors.attachment_not_found" } } },
      };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(leaveService.getMyAttachmentDownloadUrl("request-1", "doc-1")).rejects.toEqual(
        apiError
      );
    });
  });

  describe("error mapping", () => {
    it("propagates the API error when a leave request submission fails (e.g. balance/validation rejection)", async () => {
      const apiError = {
        response: { status: 400, data: { error: { message: "Yetersiz bakiye" } } },
      };
      (apiClient.post as jest.Mock).mockRejectedValue(apiError);

      await expect(
        leaveService.submitLeaveRequest({
          leaveTypeId: "type-1",
          startDate: "2026-10-01",
          endDate: "2026-10-03",
          description: null,
        })
      ).rejects.toEqual(apiError);
    });

    it("propagates the API error when fetching balances fails (e.g. no employee record)", async () => {
      const apiError = {
        response: { status: 404, data: { error: { message: "Çalışan bulunamadı" } } },
      };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(leaveService.getMyBalances()).rejects.toEqual(apiError);
    });
  });

  describe("getMyOvertimeRequests (TK-031)", () => {
    it("fetches the caller's own overtime requests", async () => {
      const mockResponse = {
        data: [
          {
            id: "ot-1",
            employeeId: "e-1",
            workDate: "2026-09-27",
            startTime: "18:00:00",
            durationMinutes: 120,
            status: "pending",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.getMyOvertimeRequests();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/leave/me/overtimes");
    });
  });

  describe("submitOvertimeRequest (TK-031 decision #4: rate never chosen by the employee)", () => {
    it("submits with the default payRateKind (rate50) when none is given", async () => {
      const mockResponse = {
        data: { id: "ot-1", overlapWithScheduleMinutes: 0, payableMinutes: 120, warnings: [] },
      };
      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      const result = await leaveService.submitOvertimeRequest({
        workDate: "2026-09-27",
        startTime: "18:00",
        durationMinutes: 120,
        description: "Ay sonu kapanışı",
      });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/overtimes", {
        workDate: "2026-09-27",
        startTime: "18:00",
        durationMinutes: 120,
        description: "Ay sonu kapanışı",
        payRateKind: "rate50",
      });
    });

    it("respects an explicit payRateKind when given", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { id: "ot-1", overlapWithScheduleMinutes: 0, payableMinutes: 60, warnings: [] },
      });

      await leaveService.submitOvertimeRequest({
        workDate: "2026-09-27",
        startTime: "09:00",
        durationMinutes: 60,
        payRateKind: "rate25",
      });

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/leave/me/overtimes",
        expect.objectContaining({ payRateKind: "rate25" })
      );
    });

    it("returns non-blocking warnings from the submission response", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: {
          id: "ot-1",
          overlapWithScheduleMinutes: 30,
          payableMinutes: 90,
          warnings: ["leave.warnings.overtime_consent_missing"],
        },
      });

      const result = await leaveService.submitOvertimeRequest({
        workDate: "2026-09-27",
        startTime: "18:00",
        durationMinutes: 120,
      });

      expect(result.warnings).toEqual(["leave.warnings.overtime_consent_missing"]);
    });
  });

  describe("withdrawOvertimeRequest (TK-031)", () => {
    it("posts to the withdraw endpoint for the given id", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await leaveService.withdrawOvertimeRequest("ot-1");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/leave/me/overtimes/ot-1/withdraw");
    });
  });
});
