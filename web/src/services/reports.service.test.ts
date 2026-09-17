import { describe, it, expect, vi, beforeEach } from "vitest";
import * as reportsService from "./reports.service";

// Mock apiClient
const mockApiClient = {
  get: vi.fn(),
  post: vi.fn(),
};

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("reports.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getReportCatalog", () => {
    it("fetches catalog from correct endpoint", async () => {
      const mockCatalog = [
        { key: "attendance-summary" },
        { key: "payroll-report" },
      ];

      mockApiClient.get.mockResolvedValueOnce({ data: mockCatalog });

      const result = await reportsService.getReportCatalog();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/reports/catalog");
      expect(result).toEqual(mockCatalog);
    });

    it("returns empty list when no reports available", async () => {
      mockApiClient.get.mockResolvedValueOnce({ data: [] });

      const result = await reportsService.getReportCatalog();

      expect(result).toEqual([]);
      expect(Array.isArray(result)).toBe(true);
    });
  });

  describe("requestReport", () => {
    it("posts report request with correct endpoint and payload", async () => {
      const mockExecution: reportsService.ReportExecution = {
        id: "exec-123",
        reportKey: "attendance-summary",
        status: "succeeded",
        requestedAt: "2026-09-17T12:00:00Z",
        completedAt: "2026-09-17T12:05:00Z",
        resultFileName: "attendance.xlsx",
        errorMessage: null,
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockExecution });

      const result = await reportsService.requestReport("attendance-summary", {
        month: "09",
        year: "2026",
      });

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/reports/executions", {
        reportKey: "attendance-summary",
        parameters: { month: "09", year: "2026" },
      });
      expect(result).toEqual(mockExecution);
    });

    it("posts report request without parameters", async () => {
      const mockExecution: reportsService.ReportExecution = {
        id: "exec-456",
        reportKey: "simple-report",
        status: "succeeded",
        requestedAt: "2026-09-17T12:00:00Z",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockExecution });

      const result = await reportsService.requestReport("simple-report");

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/reports/executions", {
        reportKey: "simple-report",
        parameters: undefined,
      });
      expect(result).toEqual(mockExecution);
    });
  });

  describe("listReportExecutions", () => {
    it("fetches execution list from correct endpoint", async () => {
      const mockExecutions: reportsService.ReportExecution[] = [
        {
          id: "exec-1",
          reportKey: "attendance",
          status: "succeeded",
          requestedAt: "2026-09-17T10:00:00Z",
          completedAt: "2026-09-17T10:05:00Z",
          resultFileName: "attendance.xlsx",
        },
        {
          id: "exec-2",
          reportKey: "payroll",
          status: "running",
          requestedAt: "2026-09-17T11:00:00Z",
        },
      ];

      const mockResult = {
        items: mockExecutions,
        totalCount: 2,
        pageIndex: 0,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockResult });

      const result = await reportsService.listReportExecutions();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/reports/executions", {
        params: {},
      });
      expect(result).toEqual(mockResult);
    });

    it("returns empty list when no executions exist", async () => {
      const mockResult = {
        items: [],
        totalCount: 0,
        pageIndex: 0,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockResult });

      const result = await reportsService.listReportExecutions({});

      expect(result.items).toEqual([]);
      expect(Array.isArray(result.items)).toBe(true);
    });

    it("passes pagination params to API", async () => {
      mockApiClient.get.mockResolvedValueOnce({
        data: { items: [], totalCount: 0, pageIndex: 1 },
      });

      await reportsService.listReportExecutions({ pageIndex: 1, pageSize: 20 });

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/reports/executions", {
        params: { pageIndex: 1, pageSize: 20 },
      });
    });
  });

  describe("null handling (WhenWritingNull)", () => {
    it("handles optional completedAt when omitted by API", async () => {
      const mockExecution = {
        id: "exec-123",
        reportKey: "report",
        status: "queued",
        requestedAt: "2026-09-17T12:00:00Z",
        // completedAt intentionally omitted (API's WhenWritingNull behavior)
      } as reportsService.ReportExecution;

      mockApiClient.post.mockResolvedValueOnce({ data: mockExecution });

      const result = await reportsService.requestReport("report");

      expect(result.completedAt).toBeUndefined();
    });

    it("handles explicit null for optional fields", async () => {
      const mockExecution: reportsService.ReportExecution = {
        id: "exec-123",
        reportKey: "report",
        status: "succeeded",
        requestedAt: "2026-09-17T12:00:00Z",
        completedAt: null,
        resultFileName: null,
        errorMessage: null,
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockExecution });

      const result = await reportsService.requestReport("report");

      expect(result.completedAt).toBeNull();
      expect(result.resultFileName).toBeNull();
      expect(result.errorMessage).toBeNull();
    });

    it("handles partial optional fields in mixed response", async () => {
      const mockExecution: reportsService.ReportExecution = {
        id: "exec-123",
        reportKey: "report",
        status: "succeeded",
        requestedAt: "2026-09-17T12:00:00Z",
        completedAt: "2026-09-17T12:05:00Z",
        resultFileName: "report.xlsx",
        // errorMessage omitted
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockExecution });

      const result = await reportsService.requestReport("report");

      expect(result.completedAt).toBe("2026-09-17T12:05:00Z");
      expect(result.resultFileName).toBe("report.xlsx");
      expect(result.errorMessage).toBeUndefined();
    });
  });

  describe("downloadReportExecution", () => {
    it("downloads from correct endpoint", async () => {
      const mockBlob = new Blob(["test data"], { type: "application/octet-stream" });
      mockApiClient.get.mockResolvedValueOnce({
        data: mockBlob,
        headers: { "content-disposition": "attachment; filename=report.xlsx" },
      });

      const result = await reportsService.downloadReportExecution("exec-123", "fallback.xlsx");

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/reports/executions/exec-123/download", {
        responseType: "blob",
      });
      expect(result.blob).toEqual(mockBlob);
    });

    it("uses fallback filename when Content-Disposition is missing", async () => {
      const mockBlob = new Blob(["test data"]);
      mockApiClient.get.mockResolvedValueOnce({
        data: mockBlob,
        headers: {},
      });

      const result = await reportsService.downloadReportExecution("exec-123", "default-name.xlsx");

      expect(result.fileName).toBe("default-name.xlsx");
    });
  });
});
