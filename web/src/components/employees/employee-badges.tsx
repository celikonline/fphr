import { useTranslation } from "react-i18next";
import { Badge, type BadgeProps } from "@mantine/core";
import type { EmployeeStatus, UserAccountStatus } from "@/services/employees.service";

type BadgeLook = Pick<BadgeProps, "variant" | "color">;

const OUTLINE: BadgeLook = { variant: "outline" };
const SECONDARY: BadgeLook = { variant: "light" };
const SUCCESS: BadgeLook = { variant: "filled", color: "green" };
const DESTRUCTIVE: BadgeLook = { variant: "filled", color: "red" };

const ACCOUNT_LOOK: Record<UserAccountStatus, BadgeLook> = {
  none: OUTLINE,
  invited: SECONDARY,
  active: SUCCESS,
  locked: DESTRUCTIVE,
  disabled: DESTRUCTIVE,
};

const STATUS_LOOK: Record<EmployeeStatus, BadgeLook> = {
  Draft: SECONDARY,
  Active: SUCCESS,
  Suspended: OUTLINE,
  Terminated: DESTRUCTIVE,
};

export function AccountStatusBadge({ status }: { status: UserAccountStatus }) {
  const { t } = useTranslation(["employees"]);
  return <Badge {...(ACCOUNT_LOOK[status] ?? OUTLINE)}>{t(`employees:accountStatus.${status}`)}</Badge>;
}

export function EmployeeStatusBadge({ status }: { status: EmployeeStatus }) {
  const { t } = useTranslation(["employees"]);
  return <Badge {...(STATUS_LOOK[status] ?? OUTLINE)}>{t(`employees:status.${status}`)}</Badge>;
}

export function initials(firstName: string, lastName: string): string {
  return `${firstName.trim().charAt(0)}${lastName.trim().charAt(0)}`.toLocaleUpperCase("tr") || "?";
}

const AVATAR_COLORS = ["blue", "cyan", "grape", "green", "indigo", "lime", "orange", "pink", "red", "teal", "violet", "yellow"];

/** Deterministic background color for an initials-only `Avatar` (no photo yet) - same `seed` always picks the same color, entirely client-side so it can't fail to load like a remote placeholder image would. */
export function avatarColor(seed: string): string {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return AVATAR_COLORS[hash % AVATAR_COLORS.length];
}
