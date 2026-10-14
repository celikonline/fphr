import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "@senseik/api-client";
import {
  acceptsPersonnelNumber,
  getPersonnelNumberAssignment,
  isPersonnelNumber,
  remainingPersonnelNumbers,
  savePersonnelNumberRange,
  savePersonnelNumberRule,
} from "./personnel-numbers.service";
vi.mock("@senseik/api-client", () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.get).mockResolvedValue({ data: {} });
  vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
  vi.mocked(apiClient.put).mockResolvedValue({ data: {} });
});
describe("personnel number contracts", () => {
  it("accepts only eight ASCII digits excluding zero", () => {
    for (const value of ["00000000", "123", "１２３４５６７８", "ABC-1234", "123456789"])
      expect(isPersonnelNumber(value)).toBe(false);
    expect(isPersonnelNumber("00000123")).toBe(true);
  });
  it("validates external bounds without converting the submitted number", () => {
    const assignment = {
      configured: true,
      assignmentMode: "external" as const,
      startNumber: "00000100",
      endNumber: "00000200",
    };
    expect(acceptsPersonnelNumber("00000100", assignment)).toBe(true);
    expect(acceptsPersonnelNumber("00000201", assignment)).toBe(false);
    expect(acceptsPersonnelNumber("00000099", assignment)).toBe(false);
    expect(acceptsPersonnelNumber("00000100", { ...assignment, assignmentMode: "internal" })).toBe(
      false
    );
    expect(acceptsPersonnelNumber("OLD-123", { configured: false })).toBe(true);
  });
  it("edits only mutable range fields and includes area scope for new rules", async () => {
    await savePersonnelNumberRange(
      {
        code: "01",
        name: "Hire",
        startNumber: "00000001",
        endNumber: "00000999",
        assignmentMode: "internal",
      },
      "r1"
    );
    expect(apiClient.put).toHaveBeenCalledWith("/v1/employees/personnel-number-ranges/r1", {
      name: "Hire",
      endNumber: "00000999",
    });
    await savePersonnelNumberRule({
      companyId: "c1",
      personnelAreaId: "a1",
      operation: "hire",
      rangeId: "r1",
      isActive: true,
    });
    expect(apiClient.post).toHaveBeenCalledWith("/v1/employees/personnel-number-rules", {
      companyId: "c1",
      personnelAreaId: "a1",
      operation: "hire",
      rangeId: "r1",
    });
  });
  it("always requests hire assignment even when org is absent", async () => {
    await getPersonnelNumberAssignment(null);
    expect(apiClient.get).toHaveBeenCalledWith("/v1/employees/personnel-number-assignment", {
      params: { orgUnitId: undefined, operation: "hire" },
    });
  });
  it("reports exhausted and initial numerical capacity", () => {
    const base = {
      id: "r1",
      code: "01",
      name: "Hire",
      startNumber: "00000100",
      endNumber: "00000199",
      assignmentMode: "internal" as const,
      isActive: true,
    };
    expect(remainingPersonnelNumbers(base)).toBe(100);
    expect(remainingPersonnelNumbers({ ...base, lastAssignedNumber: "00000199" })).toBe(0);
  });
});
