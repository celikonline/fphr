import { isRequestableOnMobile } from "@/screens/app/LeaveScreen";

describe("isRequestableOnMobile (D2)", () => {
  it("allows a plain full-day type", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: false, requiresDocument: false })
    ).toBe(true);
  });

  it("allows a half-day type without substitute/document requirements", () => {
    expect(
      isRequestableOnMobile({ unit: "halfDay", requiresSubstitute: false, requiresDocument: false })
    ).toBe(true);
  });

  it("blocks an hourly type - the submit body has no startTime/endTime fields", () => {
    expect(
      isRequestableOnMobile({ unit: "hourly", requiresSubstitute: false, requiresDocument: false })
    ).toBe(false);
  });

  it("blocks a type that requires a substitute - no substituteEmployeeId picker on mobile", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: true, requiresDocument: false })
    ).toBe(false);
  });

  it("blocks a type that requires a document - no attachment flow on mobile", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: false, requiresDocument: true })
    ).toBe(false);
  });

  it("treats a missing requiresSubstitute (older API builds) as false", () => {
    expect(isRequestableOnMobile({ unit: "fullDay", requiresDocument: false })).toBe(true);
  });
});
