import {
  canAcknowledge,
  canObject,
  payslipService,
  validateObjectionReason,
  type MyPayslip
} from "@/services/PayslipService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

const base: MyPayslip = {
  payslipId: "p1",
  year: 2026,
  month: 8,
  grossPay: 50000,
  netPay: 38000,
  publishedAt: "2026-09-01T08:00:00Z"
};

describe("PayslipService", () => {
  beforeEach(() => jest.clearAllMocks());

  it("lists the caller's own payslips", async () => {
    (apiClient.get as jest.Mock).mockResolvedValue({ data: [base] });
    expect(await payslipService.getMyPayslips()).toEqual([base]);
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/payroll/me/payslips");
  });

  it("acknowledges a payslip and returns the server row (server acknowledgedAt)", async () => {
    const row = { ...base, acknowledgedAt: "2026-09-25T10:00:00Z" };
    (apiClient.post as jest.Mock).mockResolvedValue({ data: row });
    expect(await payslipService.acknowledge("p1")).toEqual(row);
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/payroll/me/payslips/p1/acknowledge");
  });

  it("re-fetches the list when the acknowledge response has no row", async () => {
    const row = { ...base, acknowledgedAt: "2026-09-25T10:00:00Z" };
    (apiClient.post as jest.Mock).mockResolvedValue({ data: "" });
    (apiClient.get as jest.Mock).mockResolvedValue({ data: [row] });
    expect(await payslipService.acknowledge("p1")).toEqual(row);
  });

  it("object_requires_reason: rejects a blank reason without calling the API", async () => {
    await expect(payslipService.object("p1", "   ")).rejects.toThrow();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it("sends the trimmed reason on objection", async () => {
    (apiClient.post as jest.Mock).mockResolvedValue({});
    await payslipService.object("p1", "  Fazla mesai eksik ");
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/payroll/me/payslips/p1/object", {
      reason: "Fazla mesai eksik"
    });
  });

  it("validateObjectionReason trims and rejects empty", () => {
    expect(validateObjectionReason(" x ")).toBe("x");
    expect(validateObjectionReason("")).toBeNull();
  });

  describe("acknowledge_disabled_after_done", () => {
    it("is active only when published and not yet acknowledged", () => {
      expect(canAcknowledge(base)).toBe(true);
      expect(canAcknowledge({ ...base, acknowledgedAt: "2026-09-02T08:00:00Z" })).toBe(false);
      expect(canAcknowledge({ ...base, publishedAt: null })).toBe(false);
    });

    it("pending objection blocks acknowledge; resolved objection re-enables it", () => {
      expect(canAcknowledge({ ...base, objectionStatus: "pending" })).toBe(false);
      expect(canAcknowledge({ ...base, objectionStatus: "accepted" })).toBe(true);
      expect(canAcknowledge({ ...base, objectionStatus: "rejected" })).toBe(true);
      expect(
        canAcknowledge({ ...base, objectionStatus: "rejected", acknowledgedAt: "2026-09-02T08:00:00Z" })
      ).toBe(false);
    });

    it("objection is blocked while one is pending or when unpublished", () => {
      expect(canObject(base)).toBe(true);
      expect(canObject({ ...base, objectionStatus: "pending" })).toBe(false);
      expect(canObject({ ...base, publishedAt: null })).toBe(false);
    });
  });
});
