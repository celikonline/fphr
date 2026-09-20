import { describe, it, expect, beforeEach, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useVisibleItems } from "./use-nav-visibility";
import { useAuthStore } from "@/store/auth.store";
import type { AccessSettingKey } from "@/types";

// Mock the auth store
vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;

interface TestItem {
  id: string;
  label: string;
  permissions?: readonly string[];
  accessSetting?: AccessSettingKey;
}

describe("useVisibleItems", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns all items when user has no permission restrictions", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: ["admin.view", "reports.view"] },
          features: { accessSettingsBypass: true, access: {} },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Dashboard" },
      { id: "2", label: "Reports", permissions: ["reports.view"] },
      { id: "3", label: "Admin", permissions: ["admin.view"] },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(3);
    expect(result.current.map((i) => i.id)).toEqual(["1", "2", "3"]);
  });

  it("filters out items requiring permissions user doesn't have", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: ["reports.view"] },
          features: { accessSettingsBypass: true, access: {} },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Dashboard" },
      { id: "2", label: "Reports", permissions: ["reports.view"] },
      { id: "3", label: "Admin", permissions: ["admin.view", "system.manage"] },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(2);
    expect(result.current.map((i) => i.id)).toEqual(["1", "2"]);
  });

  it("shows item if user has any of multiple required permissions", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: ["payroll.view"] },
          features: { accessSettingsBypass: true, access: {} },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Payroll", permissions: ["payroll.view", "payroll.edit"] },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(1);
    expect(result.current[0].id).toBe("1");
  });

  it("filters items based on access settings", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: [] },
          features: {
            accessSettingsBypass: false,
            access: {
              showOrgInfo: true,
              showUpcomingLeaves: false,
            },
          },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Feature 1", accessSetting: "showOrgInfo" },
      { id: "2", label: "Feature 2", accessSetting: "showUpcomingLeaves" },
      { id: "3", label: "No Restriction" },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(2);
    expect(result.current.map((i) => i.id)).toEqual(["1", "3"]);
  });

  it("shows item with access setting when accessSettingsBypass is true", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: [] },
          features: {
            accessSettingsBypass: true,
            access: {},
          },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Feature", accessSetting: "showOrgInfo" },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(1);
  });

  it("hides item with access setting when accessSettingsBypass is false and access is false", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: [] },
          features: {
            accessSettingsBypass: false,
            access: {
              showOrgInfo: false,
            },
          },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Feature", accessSetting: "showOrgInfo" },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(0);
  });

  it("handles null/undefined user gracefully", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: null,
          features: { accessSettingsBypass: true, access: {} },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Dashboard" },
      { id: "2", label: "Admin", permissions: ["admin.view"] },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(1);
    expect(result.current[0].id).toBe("1");
  });

  it("handles empty items array", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: ["admin.view"] },
          features: { accessSettingsBypass: true, access: {} },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(0);
  });

  it("combines permission and access setting restrictions", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: ["feature.view"] },
          features: {
            accessSettingsBypass: false,
            access: { showOrgInfo: true },
          },
        });
      }
      return undefined;
    });

    const items: TestItem[] = [
      { id: "1", label: "Feature", permissions: ["feature.view"], accessSetting: "showOrgInfo" },
      { id: "2", label: "Dashboard" },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current).toHaveLength(2);
  });

  it("hides items whose path belongs to a module the tenant turned off (senseik-057)", () => {
    mockUseAuthStore.mockImplementation((selector) => {
      if (typeof selector === "function") {
        return selector({
          user: { permissions: [] },
          features: { accessSettingsBypass: true, access: {}, disabledModules: ["leave"] },
        });
      }
      return undefined;
    });

    const items = [
      { id: "leave", path: "/app/leave" },
      { id: "calendar", path: "/app/leave/calendar" },
      { id: "payroll", path: "/app/payroll/periods" },
      { id: "employees", path: "/app/employees" },
    ];

    const { result } = renderHook(() => useVisibleItems(items));

    expect(result.current.map((i) => i.id)).toEqual(["payroll", "employees"]);
  });
});
