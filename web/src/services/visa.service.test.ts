import { beforeEach, describe, expect, it, vi } from "vitest";
import * as visaService from "./visa.service";

const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({ apiClient: mockApiClient }));

describe("visa.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("lists only the caller's own requests, optionally filtered by status", async () => {
    mockApiClient.get.mockResolvedValue({ data: [] });

    await visaService.listMyVisaRequests();
    await visaService.listMyVisaRequests("completed");

    expect(mockApiClient.get).toHaveBeenNthCalledWith(1, "/v1/visa-requests", { params: undefined });
    expect(mockApiClient.get).toHaveBeenNthCalledWith(2, "/v1/visa-requests", { params: { status: "completed" } });
  });

  it("submits without an employee id - the API takes the requester from the signed-in user", async () => {
    mockApiClient.post.mockResolvedValue({ data: { requestId: "r1", status: "awaitingApproval", approvalRequestId: "a1" } });
    const input = {
      visaDocumentProcessId: null,
      travelStart: "2026-11-01",
      travelEnd: "2026-11-08",
      countryCode: "DE",
      visaType: "business" as const,
      description: null,
    };

    const result = await visaService.submitVisaRequest(input);

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/visa-requests", input);
    expect(Object.keys(mockApiClient.post.mock.calls[0][1])).not.toContain("employeeId");
    expect(result.status).toBe("awaitingApproval");
  });

  it("reads delivered documents from the request's documents sub-resource", async () => {
    mockApiClient.get.mockResolvedValue({ data: [] });

    await visaService.listVisaRequestDocuments("r1");

    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/visa-requests/r1/documents");
  });

  it("uses the manage sub-route for the admin process list and mutations", async () => {
    mockApiClient.get.mockResolvedValue({ data: [] });
    mockApiClient.post.mockResolvedValue({ data: {} });
    mockApiClient.put.mockResolvedValue({ data: {} });

    await visaService.listAllVisaProcesses();
    await visaService.createVisaProcess({ name: "Japonya vizesi", countryCode: "JP" });
    await visaService.updateVisaProcess("p1", { name: "Yeni ad", countryCode: null });
    await visaService.setVisaProcessActive("p1", false);

    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/visa-processes/manage");
    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/visa-processes/manage", { name: "Japonya vizesi", countryCode: "JP" });
    expect(mockApiClient.put).toHaveBeenCalledWith("/v1/visa-processes/manage/p1", { name: "Yeni ad", countryCode: null });
    expect(mockApiClient.put).toHaveBeenCalledWith("/v1/visa-processes/manage/p1/active", { isActive: false });
  });

  it("reads and writes the tenant visa settings", async () => {
    mockApiClient.get.mockResolvedValue({ data: { templateDocumentIds: [] } });
    mockApiClient.put.mockResolvedValue({ data: { templateDocumentIds: ["d1"] } });

    await visaService.getVisaSettings();
    await visaService.updateVisaSettings({ approverEmployeeId: "e1", templateDocumentIds: ["d1"] });

    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/visa-settings");
    expect(mockApiClient.put).toHaveBeenCalledWith("/v1/visa-settings", { approverEmployeeId: "e1", templateDocumentIds: ["d1"] });
  });
});
