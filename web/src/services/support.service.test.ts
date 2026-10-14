import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "@senseik/api-client";
import { addSupportTicketComment, createSupportTicket, getSupportTicket } from "./support.service";
vi.mock("@senseik/api-client", () => ({ apiClient: { post: vi.fn(), get: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());
describe("support portal bridge client", () => {
  it("reads the authorized local ticket detail for full public history", async () => {
    const ticket = { id: "local/id", portal: { comments: [] } };
    vi.mocked(apiClient.get).mockResolvedValue({ data: ticket });
    expect(await getSupportTicket("local/id")).toEqual(ticket);
    expect(apiClient.get).toHaveBeenCalledWith("/v1/support/tickets/local%2Fid");
  });
  it("posts followup using local ticket ID and caller's stable idempotency UUID", async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ status: 204 });
    await addSupportTicketComment("local/id", "Reply", "d472a820-7765-45d1-b286-0687d8486747");
    expect(apiClient.post).toHaveBeenCalledWith(
      "/v1/support/tickets/local%2Fid/comments",
      { body: "Reply" },
      { headers: { "Idempotency-Key": "d472a820-7765-45d1-b286-0687d8486747" } }
    );
  });
  it("preserves the existing local create response", async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: "local-guid" });
    expect(
      await createSupportTicket({ subject: "Help", body: "Details", category: "general" })
    ).toBe("local-guid");
  });
  it("propagates bridge failure to preserve the pending reply", async () => {
    vi.mocked(apiClient.post).mockRejectedValue(new Error("unavailable"));
    await expect(addSupportTicketComment("local-guid", "Reply", "same-key")).rejects.toThrow(
      "unavailable"
    );
  });
});
