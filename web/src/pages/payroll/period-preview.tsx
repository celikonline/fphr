import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Alert, Button, Card, Group, Table, Text, Title } from "@mantine/core";
import { FlaskConical } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import { previewPayrollPeriod, type PayrollPeriodStatus } from "@/services/payroll.service";

/**
 * "Deneme bordrosu": calculates the open period exactly as the real run would, but stores nothing - so totals and per-employee net
 * pay can be reviewed (and inputs corrected) before the payslips are generated.
 */
export function PeriodPreview({ periodId, status }: { periodId: string; status: PayrollPeriodStatus }) {
  const { t, i18n } = useTranslation(["payroll"]);
  const canRun = usePermission("payroll.period.recalculate");
  const preview = useMutation({ mutationFn: () => previewPayrollPeriod(periodId) });

  if (status !== "open" || !canRun) {
    return null;
  }

  const money = (value: number) => formatMoney(value, "TRY", i18n.language);
  const data = preview.data;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <div>
          <Title order={4}>{t("payroll:preview.title")}</Title>
          <Text size="xs" c="dimmed">
            {t("payroll:preview.subtitle")}
          </Text>
        </div>
        <Button size="xs" variant="default" leftSection={<FlaskConical size={14} />} loading={preview.isPending} onClick={() => preview.mutate()}>
          {t("payroll:preview.run")}
        </Button>
      </Group>

      {preview.isError && (
        <Alert color="red" mb="sm">
          {getApiErrorMessage(preview.error, t("payroll:preview.error"))}
        </Alert>
      )}

      {data && (
        <>
          <Group gap="xl" mb="sm" wrap="wrap">
            <Stat label={t("payroll:preview.calculated")} value={String(data.calculatedCount)} />
            <Stat label={t("payroll:preview.totalGross")} value={money(data.totalGross)} />
            <Stat label={t("payroll:preview.totalNet")} value={money(data.totalNet)} />
            <Stat label={t("payroll:preview.totalEmployerCost")} value={money(data.totalEmployerCost)} />
          </Group>
          {data.skippedCount > 0 && (
            <Alert color="yellow" mb="sm">
              {t("payroll:preview.skipped", { count: data.skippedCount })}
            </Alert>
          )}
          <Table.ScrollContainer minWidth={480}>
            <Table verticalSpacing="xs" horizontalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("payroll:preview.employee")}</Table.Th>
                  <Table.Th ta="right">{t("payroll:preview.gross")}</Table.Th>
                  <Table.Th ta="right">{t("payroll:preview.net")}</Table.Th>
                  <Table.Th ta="right">{t("payroll:preview.employerCost")}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data.lines.map((line) => (
                  <Table.Tr key={line.employeeId}>
                    <Table.Td>{line.employeeName}</Table.Td>
                    <Table.Td ta="right">{money(line.gross)}</Table.Td>
                    <Table.Td ta="right">{money(line.net)}</Table.Td>
                    <Table.Td ta="right">{money(line.employerCost)}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </>
      )}
    </Card>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text fw={600}>{value}</Text>
    </div>
  );
}
