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
