import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "@senseik/api-client";
import {
  createTerminalGroup,
  rotateTerminalApiKey,
  setCredentialActive,
  setTerminalActive,
} from "./attendance-terminals.service";

vi.mock("@senseik/api-client", () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

beforeEach(() => vi.clearAllMocks());

describe("attendance-terminals.service (real T1/T2 contract)", () => {
  it("setTerminalActive: PUT terminals/{id}/active with { isActive }", async () => {
    await setTerminalActive("t1", false);
    expect(apiClient.put).toHaveBeenCalledWith("/v1/attendance/terminals/t1/active", { isActive: false });
  });

  it("rotateTerminalApiKey: POST terminals/{id}/api-key", async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: { terminalId: "t1", apiKey: "k", apiKeyPrefix: "p" } });
    expect((await rotateTerminalApiKey("t1")).terminalId).toBe("t1");
    expect(apiClient.post).toHaveBeenCalledWith("/v1/attendance/terminals/t1/api-key");
  });

  it("createTerminalGroup: returns the bare Guid", async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: "g1" });
    expect(await createTerminalGroup("Merkez")).toBe("g1");
  });

  it("setCredentialActive: POST terminal-credentials/{id}/activate|deactivate", async () => {
    await setCredentialActive("c1", true);
    await setCredentialActive("c1", false);
    expect(apiClient.post).toHaveBeenNthCalledWith(1, "/v1/attendance/terminal-credentials/c1/activate");
    expect(apiClient.post).toHaveBeenNthCalledWith(2, "/v1/attendance/terminal-credentials/c1/deactivate");
  });
});
