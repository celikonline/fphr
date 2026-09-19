import { describe, expect, it } from "vitest";
import { toUser } from "./auth.store";
import type { MeResponse } from "@/services/me.service";

// senseik-040: "Profilim" (account.tsx) and "Kullanıcılar → Roller" (users-roles.tsx) used to show different
// Turkish text for the same role (e.g. Admin: "Yönetici" vs "Sistem Yöneticisi") because account.tsx translated
// the role CODE client-side while the Roller screen showed the backend's already-localized name. MeController now
// localizes `/me`'s roles the same way RolesController does (SharedResource.resx "roles.{code}.name"), so the
// frontend just has to carry that name through - never re-derive it - which is what `toUser` does below.
function meResponseWithRoles(roles: MeResponse["roles"]): MeResponse {
  return {
    userId: "user-1",
    email: "eren@example.com",
    firstName: "Eren",
    lastName: "Yılmaz",
    employeeId: null,
    preferredLanguage: "tr",
    mfaEnabled: false,
    tenant: {
      id: "tenant-1",
      slug: "acme",
      name: "Acme",
      taxNumber: null,
      status: "active",
      trialEndsAt: null,
      locale: "tr-TR",
      timeZone: "Europe/Istanbul",
    },
    roles,
    permissions: [],
    scopes: {},
    features: null,
  };
}

describe("toUser (senseik-040 role name consistency)", () => {
  it("carries the backend-localized role name through as roleLabels, in the same order as roles", () => {
    const me = meResponseWithRoles([
      { id: "role-hr", code: "HR", name: "İK Yöneticisi" },
      { id: "role-mgr", code: "Manager", name: "Yönetici" },
    ]);

    const user = toUser(me);

    expect(user.roles).toEqual(["HR", "Manager"]);
    // These must be the exact resx-backed strings RolesController would show for the same codes - not a
    // separately-maintained frontend translation table (that's the bug this fix removes).
    expect(user.roleLabels).toEqual(["İK Yöneticisi", "Yönetici"]);
  });

  it("falls back to the role code when the backend sends no name (never blank)", () => {
    const me = meResponseWithRoles([{ id: "role-custom", code: "CustomOpsRole", name: null }]);

    const user = toUser(me);

    expect(user.roleLabels).toEqual(["CustomOpsRole"]);
  });
});
