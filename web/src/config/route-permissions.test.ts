import { describe, expect, it } from "vitest";
import { getRouteAccess, ROUTE_ACCESS } from "./route-permissions";
import { APP_LAUNCHER_ITEMS, NAV_FOOTER_ITEMS, NAV_ITEMS, SETTINGS_PERMISSIONS, SETTINGS_SECTIONS } from "./navigation";
import type { Gated } from "@/hooks/use-nav-visibility";

type GatedPath = Gated & { path: string };

// senseik-052: the route map is derived from navigation.ts, so these tests run against the real menu config -
// a nav entry that loses its gate, or a URL spelling that slips past the lookup, must fail here.
describe("route-permissions", () => {
  it("gates the users & roles page with the same permissions as its settings card", () => {
    expect(getRouteAccess("/app/settings/system/users-roles")?.permissions).toEqual(SETTINGS_PERMISSIONS.usersRoles);
  });

  it("normalizes case and trailing slashes the way React Router matches routes", () => {
    const expected = getRouteAccess("/app/settings/system/users-roles");

    expect(getRouteAccess("/app/settings/system/users-roles/")).toEqual(expected);
    expect(getRouteAccess("/APP/Settings/System/Users-Roles")).toEqual(expected);
    expect(getRouteAccess("/app/settings/system/users-roles//")).toEqual(expected);
  });

  it("carries the access setting alongside the permission for the org chart", () => {
    expect(getRouteAccess("/app/employees/org-chart")).toEqual({
      permissions: ["employee.orgchart.view"],
      accessSetting: "showOrgChart",
    });
  });

  it("leaves ungated routes (home, account) unregistered so they stay open", () => {
    expect(getRouteAccess("/app")).toBeUndefined();
    expect(getRouteAccess("/app/account")).toBeUndefined();
  });

  it("registers every gated settings link", () => {
    const gatedLinks = SETTINGS_SECTIONS.flatMap((section): readonly GatedPath[] => section.links).filter(
      (link) => (link.permissions?.length ?? 0) > 0 || link.accessSetting !== undefined,
    );

    for (const link of gatedLinks) {
      expect(ROUTE_ACCESS.has(link.path), link.path).toBe(true);
    }
  });

  it("never declares one path with two different gates (the map would silently keep only the last)", () => {
    const seen = new Map<string, string>();
    const entries: GatedPath[] = [
      ...NAV_ITEMS,
      ...NAV_FOOTER_ITEMS,
      ...APP_LAUNCHER_ITEMS,
      ...SETTINGS_SECTIONS.flatMap((s) => s.links),
    ];

    for (const entry of entries) {
      const gate = JSON.stringify({ permissions: entry.permissions ?? [], accessSetting: entry.accessSetting ?? null });
      const previous = seen.get(entry.path);
      if (previous !== undefined) {
        expect(gate, entry.path).toBe(previous);
      }
      seen.set(entry.path, gate);
    }
  });
});
