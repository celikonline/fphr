import { useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowLeft, Download, FileSpreadsheet, Landmark, Wallet } from "lucide-react";
import { Badge, Button, Card, Group, Select, Stack, Table, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import { getCostCenters } from "@/services/organization.service";
import { ClosingChecklist } from "./closing-checklist";
import { PeriodEmptyState } from "./period-empty-state";
import { PeriodActions } from "./period-actions";
import { PeriodAdjustments } from "./period-adjustments";
import { PeriodPayItems } from "./period-pay-items";
import { PeriodComparison } from "./period-comparison";
import { PeriodPreview } from "./period-preview";
import { PeriodReceiptMatching } from "./period-receipt-matching";
import {
  downloadPayrollBankFile,
  downloadPayrollDeclarationSummary,
  downloadPayrollPeriodExport,
  getPayrollPeriod,
  saveDownloadedPayrollFile,
  type PayrollPeriodStatus,
  type PayslipLine,
} from "@/services/payroll.service";

const STATUS_COLOR: Record<PayrollPeriodStatus, string> = {
  open: "blue",
  approved: "teal",
  paid: "green",
  locked: "gray",
};

/**
 * "27/30" gösterimi; ikisi de yoksa "—". `workedDays < calendarDaysInMonth` ise turuncu (eksik gün uyarısı,
 * Faz 7 tur 2 dinamik ek md.8) - bordro rakamını değiştirmez, yalnız bilgilendirme.
 */
function formatWorkedDays(line: PayslipLine): { text: string; color?: string } {
  const { workedDays, calendarDaysInMonth } = line;
  if (workedDays == null || calendarDaysInMonth == null) {
    return { text: "—" };
  }
  return {
    text: `${workedDays}/${calendarDaysInMonth}`,
    color: workedDays < calendarDaysInMonth ? "orange" : undefined,
  };
}

/**
 * Bordro dönemi detayı - salt okunur (Faz 7 tur 1 web dilimi). Dönem başlığı + bordro satırları (brüt/net;
 * SGK/gelir vergisi/damga kırılımı yalnız Excel özetinde var, bkz. `payroll.service.ts` üst not) + "Excel indir"
 * (BE madde 4, `IReportExporter`).
 */
export default function PayrollPeriodDetailPage() {
  const canView = usePermission("payroll.period.recalculate");
  if (!canView) return <NoAccess />;
  return <PayrollPeriodDetail />;
}

function PayrollPeriodDetail() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation(["payroll", "common"]);
  const [selectedCostCenterId, setSelectedCostCenterId] = useState<string | null>(null);

  const costCentersQuery = useQuery({
    queryKey: ["organization", "cost-centers"],
    queryFn: () => getCostCenters(),
  });

  const periodQuery = useQuery({
    queryKey: ["payroll", "periods", id, selectedCostCenterId],
    queryFn: () => getPayrollPeriod(id!, selectedCostCenterId ?? undefined),
    enabled: Boolean(id),
  });

  const canExportDeclarations = usePermission("payroll.declarations.export");
  const declarationMutation = useMutation({
    mutationFn: async () => {
      const period = periodQuery.data;
      const fallbackName = period
        ? `beyanname-ozeti_${period.year}-${String(period.month).padStart(2, "0")}.csv`
        : "beyanname-ozeti.csv";
      return downloadPayrollDeclarationSummary(id!, fallbackName);
    },
    onSuccess: (file) => saveDownloadedPayrollFile(file),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("payroll:periodDetail.declarationError")),
      }),
  });

  const canExportBankFile = usePermission("payroll.bankfile.export");
  const bankFileMutation = useMutation({
    mutationFn: async () => {
      const period = periodQuery.data;
      const fallbackName = period
        ? `banka-odeme_${period.year}-${String(period.month).padStart(2, "0")}.csv`
        : "banka-odeme.csv";
      return downloadPayrollBankFile(id!, fallbackName);
    },
    onSuccess: (file) => saveDownloadedPayrollFile(file),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("payroll:periodDetail.bankFileError")),
      }),
  });

  const exportMutation = useMutation({
    mutationFn: async () => {
      const period = periodQuery.data;
      const fallbackName = period
        ? `bordro-${period.year}-${String(period.month).padStart(2, "0")}.xlsx`
        : "bordro.xlsx";
      return downloadPayrollPeriodExport(id!, fallbackName);
    },
    onSuccess: (file) => saveDownloadedPayrollFile(file),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("payroll:periodDetail.exportError")),
      }),
  });

  const payslips = useMemo(() => periodQuery.data?.payslips ?? [], [periodQuery.data]);

  if (periodQuery.isLoading) {
    return (
      <div className="w-full p-6 md:p-10">
        <Text c="dimmed">{t("common:loading")}</Text>
      </div>
    );
  }

  if (periodQuery.isError || !periodQuery.data) {
    return (
      <div className="w-full space-y-6 p-6 md:p-10">
        <Link
          to="/app/payroll/periods"
          className="inline-flex items-center gap-1 text-sm text-gray-500 hover:underline"
        >
          <ArrowLeft size={14} /> {t("payroll:periods.title")}
        </Link>
        <Text c="red">
          {getApiErrorMessage(periodQuery.error, t("payroll:periodDetail.loadError"))}
        </Text>
      </div>
    );
  }

  const period = periodQuery.data;
  const periodLabel = new Intl.DateTimeFormat(i18n.language, {
    year: "numeric",
    month: "long",
  }).format(new Date(period.year, period.month - 1, 1));

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Link
        to="/app/payroll/periods"
        className="inline-flex items-center gap-1 text-sm text-gray-500 hover:underline"
      >
        <ArrowLeft size={14} /> {t("payroll:periods.title")}
      </Link>

      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Wallet size={24} /> {periodLabel}
          </Title>
          <Badge color={STATUS_COLOR[period.status] ?? "gray"} variant="light" mt={4}>
            {t(`payroll:periods.status.${period.status}`, { defaultValue: period.status })}
          </Badge>
        </div>
        <Group gap="xs" wrap="wrap">
          <Select
            placeholder={t("payroll:periodDetail.costCenterFilter", {
              defaultValue: "Maliyet merkezi seçin...",
            })}
            data={
              costCentersQuery.data?.map((cc) => ({
                value: cc.id,
                label: cc.name,
              })) ?? []
            }
            value={selectedCostCenterId}
            onChange={setSelectedCostCenterId}
            searchable
            clearable
            loading={costCentersQuery.isLoading}
            className="min-w-48"
          />
          <Button
            type="button"
            variant="default"
            leftSection={<Download size={16} />}
            loading={exportMutation.isPending}
            onClick={() => exportMutation.mutate()}
          >
            {t("payroll:periodDetail.exportButton")}
          </Button>
          {canExportDeclarations && (
            <Button
              type="button"
              variant="default"
              leftSection={<FileSpreadsheet size={16} />}
              loading={declarationMutation.isPending}
              onClick={() => declarationMutation.mutate()}
            >
              {t("payroll:periodDetail.declarationButton")}
            </Button>
          )}
          {canExportBankFile && period.status !== "open" && (
            <Button
              type="button"
              variant="default"
              leftSection={<Landmark size={16} />}
              loading={bankFileMutation.isPending}
              onClick={() => bankFileMutation.mutate()}
            >
              {t("payroll:periodDetail.bankFileButton")}
            </Button>
          )}
        </Group>
      </Group>

      <PeriodActions periodId={period.id} status={period.status} />

      <PeriodAdjustments periodId={period.id} status={period.status} />

      <PeriodPayItems period={period} />

      <PeriodPreview periodId={period.id} status={period.status} />

      <PeriodReceiptMatching periodId={period.id} status={period.status} />

      <PeriodComparison periodId={period.id} />

      <ClosingChecklist periodId={period.id} />

      <Card withBorder padding="lg">
        {payslips.length === 0 ? (
          <PeriodEmptyState periodId={period.id} status={period.status} />
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden overflow-x-auto md:block">
              <Table verticalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("payroll:periodDetail.columns.employee")}</Table.Th>
                    <Table.Th ta="right">{t("payroll:periodDetail.workedDays")}</Table.Th>
                    <Table.Th ta="right">{t("payroll:periodDetail.columns.gross")}</Table.Th>
                    <Table.Th ta="right">{t("payroll:periodDetail.columns.net")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {payslips.map((line) => {
                    const workedDays = formatWorkedDays(line);
                    return (
                      <Table.Tr key={line.employeeId}>
                        <Table.Td>{line.employeeName}</Table.Td>
                        <Table.Td ta="right">
                          <Text size="sm" c={workedDays.color}>
                            {workedDays.text}
                          </Text>
                        </Table.Td>
                        <Table.Td ta="right">
                          {formatMoney(line.grossAmount, "TRY", i18n.language)}
                        </Table.Td>
                        <Table.Td ta="right">
                          {formatMoney(line.netAmount, "TRY", i18n.language)}
                        </Table.Td>
                      </Table.Tr>
                    );
                  })}
                </Table.Tbody>
              </Table>
            </div>

            {/* Dar ekran (≤390px dahil): kart listesi */}
            <Stack gap="sm" className="md:hidden">
              {payslips.map((line) => {
                const workedDays = formatWorkedDays(line);
                return (
                  <Card key={line.employeeId} withBorder padding="sm" radius="md">
                    <Text fw={500} size="sm">
                      {line.employeeName}
                    </Text>
                    <Group justify="space-between" mt={4}>
                      <Text size="xs" c="dimmed">
                        {t("payroll:periodDetail.workedDays")}
                      </Text>
                      <Text size="sm" c={workedDays.color}>
                        {workedDays.text}
                      </Text>
                    </Group>
                    <Group justify="space-between">
                      <Text size="xs" c="dimmed">
                        {t("payroll:periodDetail.columns.gross")}
                      </Text>
                      <Text size="sm">{formatMoney(line.grossAmount, "TRY", i18n.language)}</Text>
                    </Group>
                    <Group justify="space-between">
                      <Text size="xs" c="dimmed">
                        {t("payroll:periodDetail.columns.net")}
                      </Text>
                      <Text size="sm" fw={500}>
                        {formatMoney(line.netAmount, "TRY", i18n.language)}
                      </Text>
                    </Group>
                  </Card>
                );
              })}
            </Stack>
          </>
        )}
      </Card>
    </div>
  );
}
