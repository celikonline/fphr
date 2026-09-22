import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  PLATFORM_PERMISSIONS,
  createTenant,
  listTenants,
  type CreateTenantInput,
} from "./platform.service";

// vi.mock factories are hoisted above regular top-level statements, so the mocked value must come from vi.hoisted().
const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("platform.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("uses the platform permission key the backend defines", () => {
    expect(PLATFORM_PERMISSIONS.tenantsManage).toBe("platform.tenants.manage");
  });

  it("lists tenants from the identity tenants endpoint", async () => {
    const tenants = [
      {
        id: "1",
        slug: "acme",
        name: "Acme",
        status: "trial",
        createdAt: "2026-09-20T10:00:00Z",
        isPlatform: false,
      },
    ];
    mockApiClient.get.mockResolvedValueOnce({ data: tenants });

    await expect(listTenants()).resolves.toEqual(tenants);
    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/identity/tenants");
  });

  it("creates a tenant in invite mode without a password", async () => {
    const input: CreateTenantInput = {
      companyName: "Acme A.Ş.",
      slug: "acme",
      ownerEmail: "boss@acme.test",
      ownerFirstName: "Ali",
      ownerLastName: "Veli",
      ownerMode: "invite",
    };
    const result = { tenantId: "t1", slug: "acme", ownerUserId: "u1", invitationSent: true };
    mockApiClient.post.mockResolvedValueOnce({ data: result });

    await expect(createTenant(input)).resolves.toEqual(result);
    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/tenants", input);
    expect(mockApiClient.post.mock.calls[0][1]).not.toHaveProperty("password");
  });

  it("propagates API errors so the caller can map the problem code", async () => {
    const apiError = new Error("conflict");
    mockApiClient.post.mockRejectedValueOnce(apiError);

    await expect(
      createTenant({
        companyName: "Acme",
        slug: "acme",
        ownerEmail: "a@b.test",
        ownerFirstName: "A",
        ownerLastName: "B",
        ownerMode: "password",
        password: "Gunes-Deniz-2026",
      })
    ).rejects.toBe(apiError);
  });
});
