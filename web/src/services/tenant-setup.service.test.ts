import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  SUPPORTED_TENANT_LOCALES,
  TENANT_MANAGE_PERMISSION,
  completeTenantSetup,
  ensureOrganization,
  getTenantProfile,
  inviteUser,
  updateTenantProfile,
} from "./tenant-setup.service";

// vi.mock factories are hoisted above regular top-level statements, so the mocked value must come from vi.hoisted().
const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("tenant-setup.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("uses the permission and locale list the backend defines", () => {
    expect(TENANT_MANAGE_PERMISSION).toBe("identity.tenant.manage");
    expect(SUPPORTED_TENANT_LOCALES).toEqual(["tr-TR", "en-US", "de-DE"]);
  });

  it("reads and updates the current tenant's profile", async () => {
    const profile = { id: "t1", slug: "acme", name: "Acme", status: "Trial", locale: "tr-TR", timeZone: "Europe/Istanbul", setupCompleted: false };
    mockApiClient.get.mockResolvedValueOnce({ data: profile });
    mockApiClient.put.mockResolvedValueOnce({ data: { ...profile, name: "Acme A.Ş." } });

    await expect(getTenantProfile()).resolves.toEqual(profile);
    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/identity/tenants/current");

    const input = { name: "Acme A.Ş.", taxNumber: "123", locale: "en-US", timeZone: "Europe/Berlin" };
    await expect(updateTenantProfile(input)).resolves.toMatchObject({ name: "Acme A.Ş." });
    expect(mockApiClient.put).toHaveBeenCalledWith("/v1/identity/tenants/current", input);
  });

  it("marks setup as completed through the dedicated endpoint", async () => {
    mockApiClient.post.mockResolvedValueOnce({ data: null });

    await completeTenantSetup();

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/tenants/current/setup/complete");
  });

  it("asks the backend to prepare the Organization defaults", async () => {
    mockApiClient.post.mockResolvedValueOnce({ data: null });

    await ensureOrganization("Acme A.Ş.");

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/organization/bootstrap", { companyName: "Acme A.Ş." });
  });

  it("creates an invited user (sendInvitation is always on) and returns its id", async () => {
    mockApiClient.post.mockResolvedValueOnce({ data: "user-1" });

    const id = await inviteUser({ email: "hr@acme.test", firstName: "Ayşe", lastName: "Kaya", roleIds: ["r1"], language: "tr" });

    expect(id).toBe("user-1");
    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/users", {
      email: "hr@acme.test",
      firstName: "Ayşe",
      lastName: "Kaya",
      roleIds: ["r1"],
      language: "tr",
      sendInvitation: true,
    });
  });
});
