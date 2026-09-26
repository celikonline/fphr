import { normalizeTicketStatus } from "@/screens/app/SupportScreen";

describe("normalizeTicketStatus", () => {
  it("passes camelCase values through unchanged (the target shape after the backend fix)", () => {
    expect(normalizeTicketStatus("open")).toBe("open");
    expect(normalizeTicketStatus("answered")).toBe("answered");
    expect(normalizeTicketStatus("closed")).toBe("closed");
  });

  it("normalizes PascalCase values from the current backend (D3)", () => {
    expect(normalizeTicketStatus("Open")).toBe("open");
    expect(normalizeTicketStatus("Answered")).toBe("answered");
    expect(normalizeTicketStatus("Closed")).toBe("closed");
  });

  it("falls back to open for an unrecognized value instead of throwing", () => {
    expect(normalizeTicketStatus("SomethingElse")).toBe("open");
  });
});
