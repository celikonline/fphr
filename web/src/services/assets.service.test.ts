import { beforeEach, describe, expect, it, vi } from "vitest";
import * as assetsService from "./assets.service";
import { ASSET_STATUSES, type Asset } from "./assets.service";

const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({ apiClient: mockApiClient }));

const TODAY = "2026-10-07";

function asset(overrides: Partial<Asset> = {}): Asset {
  return {
    id: "a1",
    categoryId: "c1",
    name: "Laptop",
    serialNumber: null,
    description: null,
    status: "assigned",
    assignedEmployeeId: "e1",
    issuedAt: "2026-01-01",
    returnedAt: null,
    ...overrides,
  };
}

describe("assetsService requests", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApiClient.get.mockResolvedValue({ data: [] });
    mockApiClient.post.mockResolvedValue({ data: undefined });
  });

  it("knows every server status, including pendingAssignment and lost", () => {
    expect(ASSET_STATUSES).toEqual([
      "unassigned",
      "assigned",
      "pendingAssignment",
      "retired",
      "lost",
    ]);
  });

  it("passes the search term to the list endpoint", async () => {
    mockApiClient.get.mockResolvedValue({ data: { items: [] } });

    await assetsService.listAssets({ page: 1, pageSize: 20, status: "lost", search: "mac" });

    expect(mockApiClient.get).toHaveBeenCalledWith("/v1/assets", {
      params: { page: 1, pageSize: 20, status: "lost", search: "mac" },
    });
  });

  it("returns an asset with condition and note", async () => {
    await assetsService.returnAsset("a1", "2026-10-01", "damaged", "Ekran çatlak");

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/assets/a1/return", {
      returnedAt: "2026-10-01",
      condition: "damaged",
      note: "Ekran çatlak",
    });
  });

  it("keeps the date-only return call working for existing callers", async () => {
    await assetsService.returnAsset("a1", "2026-10-01");

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/assets/a1/return", {
      returnedAt: "2026-10-01",
      condition: undefined,
      note: undefined,
    });
  });

  it("marks an asset lost", async () => {
    await assetsService.markAssetLost("a1", "2026-10-02", "Çalındı");

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/assets/a1/lost", {
      occurredOn: "2026-10-02",
      note: "Çalındı",
    });
  });

  it("acknowledges receipt without a body", async () => {
    await assetsService.acknowledgeAsset("a1");

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/assets/a1/acknowledge");
  });

  it("reads the asset, employee, own history and the summary from the contract URLs", async () => {
    await assetsService.getAssetHistory("a1");
    await assetsService.getEmployeeAssetHistory("e1");
    await assetsService.getMyAssetHistory();
    mockApiClient.get.mockResolvedValue({ data: { total: 0 } });
    const summary = await assetsService.getAssetSummary();

    expect(mockApiClient.get.mock.calls.map((call) => call[0])).toEqual([
      "/v1/assets/a1/history",
      "/v1/assets/employees/e1/history",
      "/v1/assets/my/history",
      "/v1/assets/summary",
    ]);
    expect(summary).toEqual({ total: 0 });
  });

  it("sends the new künye fields when creating and updating an asset", async () => {
    mockApiClient.post.mockResolvedValue({ data: "new-id" });
    const fields = {
      name: "Laptop",
      serialNumber: "SN-1",
      description: null,
      assetTag: "DMB-1",
      brand: "Lenovo",
      model: "T14",
      purchaseDate: "2026-01-02",
      purchaseCost: 1500.5,
      warrantyEndsOn: "2028-01-02",
    };

    await expect(assetsService.createAsset({ ...fields, categoryId: "c1" })).resolves.toBe(
      "new-id"
    );
    await assetsService.updateAsset("a1", { ...fields, condition: "fair" });

    expect(mockApiClient.post).toHaveBeenCalledWith("/v1/assets", { ...fields, categoryId: "c1" });
    expect(mockApiClient.put).toHaveBeenCalledWith("/v1/assets/a1", {
      ...fields,
      condition: "fair",
    });
  });
});

describe("warranty helpers", () => {
  it("treats a warranty that ends today as expiring, not expired", () => {
    const today = asset({ warrantyEndsOn: TODAY });

    expect(assetsService.isWarrantyExpired(today, TODAY)).toBe(false);
    expect(assetsService.isWarrantyExpiring(today, TODAY)).toBe(true);
  });

  it("expires the day after the end date", () => {
    const yesterday = asset({ warrantyEndsOn: "2026-10-06" });

    expect(assetsService.isWarrantyExpired(yesterday, TODAY)).toBe(true);
    expect(assetsService.isWarrantyExpiring(yesterday, TODAY)).toBe(false);
  });

  it("flags up to and including day 60, not day 61", () => {
    // 2026-10-07 + 60 days = 2026-12-06
    expect(assetsService.isWarrantyExpiring(asset({ warrantyEndsOn: "2026-12-06" }), TODAY)).toBe(
      true
    );
    expect(assetsService.isWarrantyExpiring(asset({ warrantyEndsOn: "2026-12-07" }), TODAY)).toBe(
      false
    );
    expect(assetsService.isWarrantyExpired(asset({ warrantyEndsOn: "2026-12-07" }), TODAY)).toBe(
      false
    );
  });

  it("ignores assets without a warranty date and retired or lost assets", () => {
    expect(assetsService.isWarrantyExpired(asset(), TODAY)).toBe(false);
    expect(assetsService.isWarrantyExpiring(asset({ warrantyEndsOn: null }), TODAY)).toBe(false);
    for (const status of ["retired", "lost"] as const) {
      expect(
        assetsService.isWarrantyExpired(asset({ status, warrantyEndsOn: "2020-01-01" }), TODAY)
      ).toBe(false);
      expect(
        assetsService.isWarrantyExpiring(asset({ status, warrantyEndsOn: TODAY }), TODAY)
      ).toBe(false);
    }
  });

  it("still follows up unassigned and pending assets", () => {
    for (const status of ["unassigned", "pendingAssignment"] as const) {
      expect(
        assetsService.isWarrantyExpired(asset({ status, warrantyEndsOn: "2020-01-01" }), TODAY)
      ).toBe(true);
    }
  });
});
