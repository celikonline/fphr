import {
  canSubmitOvertimeRequest,
  overtimePayRateLabel,
} from "@/components/requests/OvertimeRequestSection";
import { OVERTIME_DEFAULT_PAY_RATE_KIND } from "@/services/LeaveService";

describe("canSubmitOvertimeRequest (TK-031)", () => {
  it("allows a valid duration", () => {
    expect(canSubmitOvertimeRequest({ durationText: "120" })).toBe(true);
  });

  it("blocks a zero duration", () => {
    expect(canSubmitOvertimeRequest({ durationText: "0" })).toBe(false);
  });

  it("blocks a duration over 24 hours (1440 minutes)", () => {
    expect(canSubmitOvertimeRequest({ durationText: "1441" })).toBe(false);
  });

  it("blocks an unparseable duration", () => {
    expect(canSubmitOvertimeRequest({ durationText: "" })).toBe(false);
  });

  it("blocks a non-integer duration", () => {
    expect(canSubmitOvertimeRequest({ durationText: "30.5" })).toBe(false);
  });
});

describe("OVERTIME_PAY_RATE_LABELS (TK-031 web parity: pay rate selection)", () => {
  it("labels both pay rate options in Turkish", () => {
    expect(overtimePayRateLabel("rate50")).toBe("%50 fazlası (fazla mesai)");
    expect(overtimePayRateLabel("rate25")).toBe("%25 fazlası (fazla sürelerle çalışma)");
  });

  it("defaults to rate50, matching the service default", () => {
    expect(OVERTIME_DEFAULT_PAY_RATE_KIND).toBe("rate50");
  });
});
