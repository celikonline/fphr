import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Skeleton, Table, Text, Title } from "@mantine/core";
import { Download } from "lucide-react";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import {
  downloadPayslipDocument,
  listEmployeePayslips,
  saveDownloadedPayrollFile,
  type EmployeePayslip,
} from "@/services/payroll.service";

const PERIOD_STATUS_COLOR: Record<EmployeePayslip["periodStatus"], string> = {
  open: "gray",
  approved: "blue",
  paid: "green",
  locked: "dark",
};

/** "Ücret pusulaları": the employee's calculated payslips by period, each downloadable as the m.37 pusula text. */
export function EmployeePayslipsCard({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const { data, isLoading } = useQuery({
    queryKey: ["employee-payslips", employeeId],
    queryFn: () => listEmployeePayslips(employeeId),
    retry: false,
  });

  const download = useMutation({
    mutationFn: (payslip: EmployeePayslip) =>
      downloadPayslipDocument(
        payslip.payslipId,
        `ucret-pusulasi_${payslip.year}-${String(payslip.month).padStart(2, "0")}.pdf`
      ),
    onSuccess: saveDownloadedPayrollFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("employees:payrollTab.payslips.downloadError")),
      }),
  });

  const money = (amount: number) => formatMoney(amount, "TRY", i18n.language);
  const period = (payslip: EmployeePayslip) =>
    new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(
      new Date(payslip.year, payslip.month - 1, 1)
    );

  return (
    <Card withBorder padding="lg">
      <Title order={4} mb="md">
        {t("employees:payrollTab.payslips.title")}
      </Title>
      {isLoading ? (
        <Skeleton h={96} />
      ) : !data || data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:payrollTab.payslips.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("employees:payrollTab.payslips.columns.period")}</Table.Th>
                <Table.Th>{t("employees:payrollTab.payslips.columns.status")}</Table.Th>
                <Table.Th ta="right">{t("employees:payrollTab.payslips.columns.gross")}</Table.Th>
                <Table.Th ta="right">{t("employees:payrollTab.payslips.columns.net")}</Table.Th>
                <Table.Th ta="right">
                  {t("employees:payrollTab.payslips.columns.employerCost")}
                </Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((payslip) => (
                <Table.Tr key={payslip.payslipId}>
                  <Table.Td tt="capitalize">{period(payslip)}</Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={PERIOD_STATUS_COLOR[payslip.periodStatus]}>
                      {t(`employees:payrollTab.enum.periodStatus.${payslip.periodStatus}`)}
                    </Badge>
                  </Table.Td>
                  <Table.Td ta="right">{money(payslip.grossAmount)}</Table.Td>
                  <Table.Td ta="right">{money(payslip.netAmount)}</Table.Td>
                  <Table.Td ta="right">{money(payslip.employerCost)}</Table.Td>
                  <Table.Td>
                    <Group justify="flex-end">
                      <Button
                        variant="subtle"
                        size="xs"
                        leftSection={<Download size={14} />}
                        loading={
                          download.isPending && download.variables?.payslipId === payslip.payslipId
                        }
                        onClick={() => download.mutate(payslip)}
                      >
                        {t("employees:payrollTab.payslips.download")}
                      </Button>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}
