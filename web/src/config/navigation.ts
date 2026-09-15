import type { LucideIcon } from "lucide-react";
import { Building2, Network, MapPin, Briefcase, Users, ScrollText } from "lucide-react";

export interface NavItem {
  /** i18n key in the "navigation" namespace. */
  labelKey: string;
  path: string;
  icon: LucideIcon;
  /** At least one of these permissions is required to see the item (empty = always visible). */
  permissions?: string[];
}

export const NAV_ITEMS: NavItem[] = [
  {
    labelKey: "nav.companyProfile",
    path: "/app/company-profile",
    icon: Building2,
    permissions: ["identity.tenant.view", "identity.tenant.manage"],
  },
  {
    labelKey: "nav.organization",
    path: "/app/organization",
    icon: Network,
    permissions: ["organization.units.view", "organization.units.manage"],
  },
  {
    labelKey: "nav.locations",
    path: "/app/locations",
    icon: MapPin,
    permissions: ["organization.locations.view", "organization.locations.manage"],
  },
  {
    labelKey: "nav.positions",
    path: "/app/positions",
    icon: Briefcase,
    permissions: ["organization.positions.view", "organization.positions.manage"],
  },
  {
    labelKey: "nav.usersRoles",
    path: "/app/users-roles",
    icon: Users,
    permissions: ["identity.users.view", "identity.roles.view"],
  },
  {
    labelKey: "nav.auditLog",
    path: "/app/audit-log",
    icon: ScrollText,
    permissions: ["identity.audit.view"],
  },
];
