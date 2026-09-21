import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Card, Group, Text, Title } from "@mantine/core";
import { useModuleEnabled } from "@/hooks/use-module-enabled";
import { formatDateOnly } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { getExitSettlement } from "@/services/payroll-severance.service";
import type { EmployeeDetail } from "@/services/employees.service";

/**
 * "Çıkış hesabı": the kıdem/ihbar/izin amounts Payroll froze when the employee was terminated. Unlike the wizard's
 * preview it is a record, so later salary or legislation changes never move it. Hidden for active employees, when the
 * Payroll module is off, and when no settlement was recorded (e.g. exits before the feature existed).
 */
export function ExitSettlementSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees"]);
  const payrollEnabled = useModuleEnabled("payroll");
  const isTerminated = employee.status === "Terminated";

  const { data } = useQuery({
    queryKey: ["exit-settlement", employee.id],
    queryFn: () => getExitSettlement(employee.id),
    enabled: payrollEnabled && isTerminated,
    retry: false,
  });

  if (!payrollEnabled || !isTerminated || !data) {
    return null;
  }

  const money = (amount: number) => formatMoney(amount, data.currency, i18n.language);
  const rows = [
    { key: "severance", label: t("employees:exitSettlement.severance"), value: money(data.severanceAmount) },
    { key: "notice", label: t("employees:exitSettlement.notice"), value: money(data.noticeAmount) },
    { key: "unusedLeave", label: t("employees:exitSettlement.unusedLeave"), value: money(data.unusedLeaveAmount) },
  ];

  return (
    <Card withBorder padding="lg">
      <Title order={4}>{t("employees:exitSettlement.title")}</Title>
      <Text size="xs" c="dimmed" mb="md">
        {t("employees:exitSettlement.hint")}
      </Text>
      <Group justify="space-between" mb="xs">
        <Text size="sm" c="dimmed">
          {t("employees:exitSettlement.terminationDate")}
        </Text>
        <Text size="sm">{formatDateOnly(data.terminationDate, i18n.language)}</Text>
      </Group>
      {rows.map((row) => (
        <Group key={row.key} justify="space-between" mb="xs">
          <Text size="sm">{row.label}</Text>
          <Text size="sm">{row.value}</Text>
        </Group>
      ))}
      <Group justify="space-between" mt="sm">
        <Text size="sm" fw={600}>
          {t("employees:exitSettlement.total")}
        </Text>
        <Text size="sm" fw={600}>
          {money(data.totalAmount)}
        </Text>
      </Group>
    </Card>
  );
}
