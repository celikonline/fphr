import { describe, expect, it } from "vitest";
import { NAV_ITEMS } from "./navigation";
import { getRouteAccess } from "./route-permissions";

describe("platform (super admin) route", () => {
  it("is gated behind platform.tenants.manage, so a direct URL visit is refused without it", () => {
    expect(getRouteAccess("/app/platform/tenants")?.permissions).toEqual([
      "platform.tenants.manage",
    ]);
  });

  it("keeps the guard for case and trailing-slash variants of the URL", () => {
    expect(getRouteAccess("/APP/Platform/Tenants/")?.permissions).toEqual([
      "platform.tenants.manage",
    ]);
  });

  it("appears in the sidebar only for holders of the platform permission", () => {
    const item = NAV_ITEMS.find((entry) => entry.key === "platformTenants");

    expect(item?.path).toBe("/app/platform/tenants");
    expect(item?.permissions).toEqual(["platform.tenants.manage"]);
  });
});
