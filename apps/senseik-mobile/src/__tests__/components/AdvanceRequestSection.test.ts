import { canSubmitAdvanceRequest } from "@/components/requests/AdvanceRequestSection";

describe("canSubmitAdvanceRequest (TK-031)", () => {
  const today = new Date().toISOString().split("T")[0];
  const validParams = { amountText: "1000", requestedDate: today };

  it("allows a valid submission", () => {
    expect(canSubmitAdvanceRequest(validParams)).toBe(true);
  });

  it("blocks a zero amount", () => {
    expect(canSubmitAdvanceRequest({ ...validParams, amountText: "0" })).toBe(false);
  });

  it("blocks a negative amount", () => {
    expect(canSubmitAdvanceRequest({ ...validParams, amountText: "-5" })).toBe(false);
  });

  it("blocks an unparseable amount", () => {
    expect(canSubmitAdvanceRequest({ ...validParams, amountText: "" })).toBe(false);
  });

  it("blocks a requested date more than 90 days in the future", () => {
    const farFuture = new Date();
    farFuture.setDate(farFuture.getDate() + 200);
    const [dateOnly] = farFuture.toISOString().split("T");
    expect(canSubmitAdvanceRequest({ ...validParams, requestedDate: dateOnly })).toBe(false);
  });

  it("blocks a requested date more than 30 days in the past", () => {
    const farPast = new Date();
    farPast.setDate(farPast.getDate() - 200);
    const [dateOnly] = farPast.toISOString().split("T");
    expect(canSubmitAdvanceRequest({ ...validParams, requestedDate: dateOnly })).toBe(false);
  });
});
