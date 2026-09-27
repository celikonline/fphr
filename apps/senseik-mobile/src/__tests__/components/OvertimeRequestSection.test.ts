import { canSubmitOvertimeRequest } from "@/components/requests/OvertimeRequestSection";

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
