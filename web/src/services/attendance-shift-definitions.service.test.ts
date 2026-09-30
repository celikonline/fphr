import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "@senseik/api-client";
import { emptyShiftDefinition } from "@/components/attendance/shift-definition-form";
import {
  listShiftDefinitions,
  saveShiftDefinition,
  setShiftDefinitionActive,
} from "./attendance-shift-definitions.service";

vi.mock("@senseik/api-client", () => ({ apiClient: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }));

describe("attendance-shift-definitions.service", () => {
  beforeEach(() => vi.clearAllMocks());

  it("PUT body carries neither id nor isActive and returns the save result", async () => {
    const result = { id: "s1", recalculationJobId: "j1", affectedAssignmentCount: 3 };
    vi.mocked(apiClient.put).mockResolvedValue({ data: result });
    const out = await saveShiftDefinition({ ...emptyShiftDefinition(), id: "s1" }, { applyToPast: true });
    const [url, body, config] = vi.mocked(apiClient.put).mock.calls[0];
    expect(url).toBe("/v1/attendance/shift-definitions/s1");
    expect(body).not.toHaveProperty("isActive");
    expect(body).not.toHaveProperty("id");
    expect(config).toEqual({ params: { applyToPast: true } });
    expect(out).toEqual(result);
  });

  it("POST returns the Guid wrapped as a save result", async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ data: "new-id" });
    const out = await saveShiftDefinition(emptyShiftDefinition(), { applyToPast: false });
    expect(out).toEqual({ id: "new-id", affectedAssignmentCount: 0 });
  });

  it("active toggle uses the dedicated endpoint; list sends includeInactive only when asked", async () => {
    vi.mocked(apiClient.put).mockResolvedValue({ data: undefined });
    await setShiftDefinitionActive("s1", false);
    expect(apiClient.put).toHaveBeenCalledWith("/v1/attendance/shift-definitions/s1/active", { isActive: false });
    vi.mocked(apiClient.get).mockResolvedValue({ data: [] });
    await listShiftDefinitions();
    await listShiftDefinitions({ includeInactive: true });
    expect(vi.mocked(apiClient.get).mock.calls[0][1]).toEqual({ params: undefined });
    expect(vi.mocked(apiClient.get).mock.calls[1][1]).toEqual({ params: { includeInactive: true } });
  });
});
