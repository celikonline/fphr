import { useTranslation } from "react-i18next";
import { Badge } from "@/components/ui/badge";
import type { EmployeeStatus, UserAccountStatus } from "@/services/employees.service";

const ACCOUNT_VARIANT: Record<UserAccountStatus, "success" | "secondary" | "destructive" | "outline"> = {
  none: "outline",
  invited: "secondary",
  active: "success",
  locked: "destructive",
  disabled: "destructive",
};

const STATUS_VARIANT: Record<EmployeeStatus, "success" | "secondary" | "outline"> = {
  Active: "success",
  OnLeave: "secondary",
  Inactive: "outline",
};

export function AccountStatusBadge({ status }: { status: UserAccountStatus }) {
  const { t } = useTranslation(["employees"]);
  return <Badge variant={ACCOUNT_VARIANT[status] ?? "outline"}>{t(`employees:accountStatus.${status}`)}</Badge>;
}

export function EmployeeStatusBadge({ status }: { status: EmployeeStatus }) {
  const { t } = useTranslation(["employees"]);
  return <Badge variant={STATUS_VARIANT[status] ?? "outline"}>{t(`employees:status.${status}`)}</Badge>;
}

export function initials(firstName: string, lastName: string): string {
  return `${firstName.trim().charAt(0)}${lastName.trim().charAt(0)}`.toLocaleUpperCase("tr") || "?";
}
