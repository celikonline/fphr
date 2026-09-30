import {
  formatMoney,
  payslipStatusLabel,
  buildPayslipFileName,
  formatAcknowledgedLabel,
  formatPayslipPeriod,
  payslipAcknowledgeErrorMessage,
  payslipAcknowledgementStatus,
  payslipOpenErrorMessage,
  payslipsLoadErrorMessage,
  shouldIgnorePayslipTap,
} from "@/screens/app/PayrollScreen";
import i18n from "@/lib/i18n";

const base = { payslipId: "p", year: 2026, month: 8, grossPay: 1, netPay: 1, publishedAt: "2026-09-01" };

describe("PayrollScreen list_shows_ack_status", () => {
  it("shows the acknowledgement / objection state", () => {
    expect(payslipStatusLabel(base)).toBe("Onay bekliyor");
    expect(payslipStatusLabel({ ...base, acknowledgedAt: "2026-09-02" })).toBe("Onaylandı");
    expect(payslipStatusLabel({ ...base, objectionStatus: "pending" })).toBe("İtiraz inceleniyor");
    expect(payslipStatusLabel({ ...base, objectionStatus: "accepted" })).toBe("İtiraz kabul edildi");
  });
});

describe("formatMoney", () => {
  it("uses the payslip currency, defaulting to TRY", () => {
    expect(formatMoney(1234.5, "EUR")).toMatch(/€/);
    expect(formatMoney(1234.5, undefined)).toMatch(/₺|TRY/);
    expect(formatMoney(1234.5, null)).toMatch(/₺|TRY/);
  });
});

describe("buildPayslipFileName (TK-030)", () => {
  it("builds a stable, zero-padded filename", () => {
    expect(buildPayslipFileName({ year: 2026, month: 9 })).toBe("bordro-2026-09.pdf");
  });

  it("does not zero-pad a two-digit month unnecessarily", () => {
    expect(buildPayslipFileName({ year: 2026, month: 12 })).toBe("bordro-2026-12.pdf");
  });
});

describe("shouldIgnorePayslipTap (double-tap guard, TK-030)", () => {
  it("ignores a tap while another payslip is opening", () => {
    expect(shouldIgnorePayslipTap("payslip-1")).toBe(true);
  });

  it("allows a tap when nothing is currently opening", () => {
    expect(shouldIgnorePayslipTap(null)).toBe(false);
  });
});

describe("payslipsLoadErrorMessage (TK-030 riskli alan: never leak the server's error detail)", () => {
  it("returns the same generic Turkish message regardless of the underlying error", () => {
    const message = "Bordro pusulaları yüklenemedi. Lütfen tekrar deneyin.";
    expect(payslipsLoadErrorMessage({ response: { status: 403 } })).toBe(message);
    expect(
      payslipsLoadErrorMessage({ response: { status: 404, data: { detail: "Gizli ayrıntı" } } })
    ).toBe(message);
    expect(payslipsLoadErrorMessage(new Error("Network Error"))).toBe(message);
    expect(payslipsLoadErrorMessage(undefined)).toBe(message);
  });

  it("never echoes back any text from the error object", () => {
    const sensitiveError = {
      response: { status: 404, data: { detail: "Bu talebe ait maaş bilgisi: 45000 TRY" } },
    };
    expect(payslipsLoadErrorMessage(sensitiveError)).not.toContain("45000");
    expect(payslipsLoadErrorMessage(sensitiveError)).not.toContain("maaş");
  });
});

describe("payslipOpenErrorMessage (TK-030 riskli alan)", () => {
  it("returns the same generic Turkish message regardless of the underlying error", () => {
    const message = "Bordro pusulası açılamadı. Lütfen tekrar deneyin.";
    expect(payslipOpenErrorMessage({ response: { status: 403 } })).toBe(message);
    expect(payslipOpenErrorMessage({ response: { status: 404 } })).toBe(message);
    expect(payslipOpenErrorMessage(new Error("boom"))).toBe(message);
  });
});

describe("payslipAcknowledgeErrorMessage (TK-030 kabul kriteri 2, riskli alan)", () => {
  it("returns the same generic Turkish message regardless of the underlying error", () => {
    const message = "Onay gönderilemedi. Lütfen tekrar deneyin.";
    expect(payslipAcknowledgeErrorMessage({ response: { status: 403 } })).toBe(message);
    expect(payslipAcknowledgeErrorMessage({ response: { status: 404 } })).toBe(message);
    expect(payslipAcknowledgeErrorMessage(new Error("boom"))).toBe(message);
  });

  it("never echoes back any text from the error object", () => {
    const sensitiveError = {
      response: { status: 404, data: { detail: "Bu talebe ait maaş bilgisi: 45000 TRY" } },
    };
    expect(payslipAcknowledgeErrorMessage(sensitiveError)).not.toContain("45000");
  });
});

describe("formatAcknowledgedLabel (TK-030 kabul kriteri 2)", () => {
  it("prefixes the formatted date with 'Onaylandı ·'", () => {
    // Far enough in the past that `formatRelativeTime` always falls back to an absolute date,
    // regardless of when this test runs - keeps the assertion deterministic without mocking the clock.
    const acknowledgedAt = "2020-01-15T10:00:00Z";
    const expectedDate = new Date(acknowledgedAt).toLocaleDateString("tr-TR", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });

    expect(formatAcknowledgedLabel(acknowledgedAt)).toBe(`Onaylandı · ${expectedDate}`);
  });
});

describe("payslipAcknowledgementStatus (TK-030, mobil yeniden tasarım)", () => {
  it("returns the acknowledged pill when the payslip has an acknowledgedAt", () => {
    expect(payslipAcknowledgementStatus("2026-09-01T10:00:00Z")).toEqual({
      label: "Onaylandı",
      tone: "success",
    });
  });

  it("returns the pending pill when the payslip has not been acknowledged", () => {
    expect(payslipAcknowledgementStatus(null)).toEqual({
      label: "Onay bekliyor",
      tone: "warning",
    });
  });
});

describe("PayrollScreen helpers follow the active language (FG-001b)", () => {
  it("formats the period, statuses and generic errors in English", async () => {
    await i18n.changeLanguage("en");

    expect(formatPayslipPeriod(2026, 9)).toBe("September 2026");
    expect(payslipAcknowledgementStatus(null)).toEqual({
      label: "Awaiting acknowledgement",
      tone: "warning",
    });
    expect(payslipAcknowledgementStatus("2026-09-01T10:00:00Z").label).toBe("Acknowledged");
    expect(formatAcknowledgedLabel("2020-01-15T10:00:00Z")).toMatch(/^Acknowledged · /);
    expect(payslipsLoadErrorMessage({ response: { status: 403 } })).toBe(
      "Payslips could not be loaded. Please try again."
    );
    expect(payslipOpenErrorMessage(new Error("boom"))).toBe(
      "The payslip could not be opened. Please try again."
    );
    expect(payslipAcknowledgeErrorMessage(new Error("boom"))).toBe(
      "The acknowledgement could not be sent. Please try again."
    );
  });

  it("falls back to the month number when it is out of range", () => {
    expect(formatPayslipPeriod(2026, 13)).toBe("13 2026");
  });
});
