import { humanizeLeaveTypeCode, leaveTypeDisplayName } from "@/lib/leaveTypeNames";

describe("leaveTypeDisplayName", () => {
  it("resolves a seeded leave-type resource key to its Turkish name", () => {
    expect(leaveTypeDisplayName("leave.type.annual")).toBe("Yıllık izin");
  });

  it("passes a free-form (user-entered custom type) name through unchanged", () => {
    expect(leaveTypeDisplayName("Özel izin türüm")).toBe("Özel izin türüm");
  });
});

describe("humanizeLeaveTypeCode (B5 fallback when leaveTypeName is missing)", () => {
  it("capitalizes a single-word code", () => {
    expect(humanizeLeaveTypeCode("annual")).toBe("Annual");
  });

  it("splits a camelCase code into words", () => {
    expect(humanizeLeaveTypeCode("postnatalUnpaid")).toBe("Postnatal Unpaid");
  });

  it("returns an empty string unchanged", () => {
    expect(humanizeLeaveTypeCode("")).toBe("");
  });
});
