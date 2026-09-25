import { payslipStatusLabel } from "@/screens/app/PayrollScreen";

const base = { payslipId: "p", year: 2026, month: 8, grossPay: 1, netPay: 1, publishedAt: "2026-09-01" };

describe("PayrollScreen list_shows_ack_status", () => {
  it("shows the acknowledgement / objection state", () => {
    expect(payslipStatusLabel(base)).toBe("Onay bekliyor");
    expect(payslipStatusLabel({ ...base, acknowledgedAt: "2026-09-02" })).toBe("Onaylandı");
    expect(payslipStatusLabel({ ...base, objectionStatus: "pending" })).toBe("İtiraz inceleniyor");
    expect(payslipStatusLabel({ ...base, objectionStatus: "accepted" })).toBe("İtiraz kabul edildi");
  });
});
