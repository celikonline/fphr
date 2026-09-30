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

/** TK-030 `GET payroll/payslips/my` row for `base` (null fields omitted by the API). */
const apiRow = {
  payslipId: "p1",
  year: 2026,
  month: 8,
  grossAmount: 50000,
  netAmount: 38000,
  hasDocument: true,
  createdAt: "2026-09-01T08:00:00Z"
};
const mapped: MyPayslip = {
  ...base,
  currency: "TRY",
  acknowledgedAt: null,
  objectionStatus: null,
  objectionReason: null
};

describe("PayslipService", () => {
  beforeEach(() => jest.clearAllMocks());

  it("lists the caller's own payslips from the TK-030 endpoint", async () => {
    (apiClient.get as jest.Mock).mockResolvedValue({ data: [apiRow] });
    expect(await payslipService.getMyPayslips()).toEqual([mapped]);
    expect(apiClient.get).toHaveBeenCalledWith("/api/v1/payroll/payslips/my");
  });

  it("maps the objection state of the TK-030 row", async () => {
    (apiClient.get as jest.Mock).mockResolvedValue({
      data: [{ ...apiRow, objectionStatus: "pending", objectionReason: "Prim eksik" }]
    });
    const [row] = await payslipService.getMyPayslips();
    expect(row.objectionStatus).toBe("pending");
    expect(row.objectionReason).toBe("Prim eksik");
  });

  it("acknowledges via TK-030 (mobile channel) and returns the re-fetched server row", async () => {
    (apiClient.post as jest.Mock).mockResolvedValue({ data: "" });
    (apiClient.get as jest.Mock).mockResolvedValue({
      data: [{ ...apiRow, acknowledgedAt: "2026-09-25T10:00:00Z" }]
    });
    expect(await payslipService.acknowledge("p1")).toEqual({ ...mapped, acknowledgedAt: "2026-09-25T10:00:00Z" });
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/payroll/payslips/my/p1/acknowledge", {
      channel: "mobile"
    });
  });

  it("object_requires_reason: rejects a blank reason without calling the API", async () => {
    await expect(payslipService.object("p1", "   ")).rejects.toThrow();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it("sends the trimmed reason on objection", async () => {
    (apiClient.post as jest.Mock).mockResolvedValue({});
    await payslipService.object("p1", "  Fazla mesai eksik ");
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/payroll/payslips/my/p1/object", {
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

    it("objection is blocked after a resolved objection or an acknowledgement (one per version)", () => {
      expect(canObject({ ...base, objectionStatus: "accepted" })).toBe(false);
      expect(canObject({ ...base, objectionStatus: "rejected" })).toBe(false);
      expect(canObject({ ...base, acknowledgedAt: "2026-09-02T08:00:00Z" })).toBe(false);
    });
  });
});
