import {
  buildPayslipFileName,
  formatPayslipPeriod,
  payslipOpenErrorMessage,
  payslipsLoadErrorMessage,
  shouldIgnorePayslipTap,
} from "@/screens/app/PayrollScreen";

describe("formatPayslipPeriod (TK-030)", () => {
  it("formats a period as 'Ay Yıl'", () => {
    expect(formatPayslipPeriod(2026, 9)).toBe("Eylül 2026");
  });

  it("formats January correctly (1-based month)", () => {
    expect(formatPayslipPeriod(2026, 1)).toBe("Ocak 2026");
  });

  it("formats December correctly", () => {
    expect(formatPayslipPeriod(2026, 12)).toBe("Aralık 2026");
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
    expect(payslipsLoadErrorMessage({ response: { status: 404, data: { detail: "Gizli ayrıntı" } } })).toBe(
      message
    );
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
