import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ActionIcon, Badge, Button, Checkbox, Group, Stack, Switch, Text, Title, Tooltip } from "@mantine/core";
import { ChevronLeft, ChevronRight, Download, NotebookText } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { downloadReportExecution, requestReport, saveDownloadedReportFile } from "@/services/reports.service";
import { getCompanyLeaveLedger, type CompanyLeaveLedgerRow } from "@/services/leave.service";

const REPORT_KEY = "leave.company_ledger";

/** First day of the calendar month that contains `date` (local time). */
function monthStartOf(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/**
 * senseik-048: "İzinlerim"/"Fazla mesailerim" yanında, İK Müdürü/Hesap Sahibi için şirket geneli görünüm - "bu ay
 * kimler izinde", "yıllık izin birikimi en yüksek 10 kişi", "270 saati aşan fazla mesai" sorularını tek, filtrelenebilir
 * bir tabloda cevaplar (leave.company_ledger.read). Takvim (leave-calendar.tsx) ile tamamlayıcı: bu, çalışan başına
 * bir satır olan bir liste - gün bazlı bir takvim değil. Sıralama sütun başlığına tıklayarak yapılır (ör. "Yıllık izin
 * bakiyesi"nde azalan sıraya göre en üstteki 10 satır = en yüksek birikim); "Sadece..." anahtarları hazır filtreler.
 */
export default function CompanyLeaveLedgerPage() {
  const canView = usePermission("leave.company_ledger.read");
  if (!canView) return <NoAccess />;
  return <CompanyLeaveLedger />;
}

function CompanyLeaveLedger() {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const [monthStart, setMonthStart] = useState(() => monthStartOf(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [includeTerminated, setIncludeTerminated] = useState(false);
  const [onlyOnLeave, setOnlyOnLeave] = useState(false);
  const [onlyOverLimit, setOnlyOverLimit] = useState(false);

  const year = monthStart.getFullYear();
  const month = monthStart.getMonth() + 1;

  const query = useQuery({
    queryKey: ["leave", "company-ledger", year, month, orgUnitId, includeTerminated],
    queryFn: () => getCompanyLeaveLedger({ year, month, orgUnitId, includeTerminated }),
  });

  const rows = useMemo(() => {
    let list = query.data?.rows ?? [];
    if (onlyOnLeave) list = list.filter((row) => row.isOnLeaveInPeriod);
    if (onlyOverLimit) list = list.filter((row) => row.overtimeLimitExceeded);
    return list;
  }, [query.data, onlyOnLeave, onlyOverLimit]);

  const exportMutation = useMutation({
    mutationFn: async () => {
      const execution = await requestReport(REPORT_KEY, {
        year: String(year),
        month: String(month),
        includeTerminated: String(includeTerminated),
        ...(orgUnitId ? { orgUnitIds: orgUnitId } : {}),
      });
      if (execution.status === "succeeded") {
        saveDownloadedReportFile(await downloadReportExecution(execution.id, execution.resultFileName ?? `${REPORT_KEY}.xlsx`));
      }
      return execution;
    },
    onSuccess: (execution) => {
      if (execution.status === "failed") {
        toast({ variant: "destructive", title: t("common:error"), description: execution.errorMessage ?? t("leave:companyLedger.exportError") });
        return;
      }
      toast({ variant: "success", title: t("common:success"), description: t("leave:companyLedger.exportSuccess") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:companyLedger.exportError")) }),
  });

  const columns: ColumnDef<CompanyLeaveLedgerRow>[] = [
    {
      id: "name",
      accessorFn: (row) => `${row.firstName} ${row.lastName}`.trim(),
      header: t("leave:companyLedger.columns.employee"),
      cell: ({ row }) => (
        <Text size="sm" fw={500}>
          {row.original.firstName} {row.original.lastName}
        </Text>
      ),
    },
    {
      id: "orgPath",
      accessorFn: (row) => row.orgPath ?? "",
      header: t("leave:companyLedger.columns.unit"),
      cell: ({ row }) => row.original.orgPath || "—",
    },
    {
      id: "onLeave",
      accessorFn: (row) => row.isOnLeaveInPeriod,
      header: t("leave:companyLedger.columns.onLeave"),
      cell: ({ row }) => {
        const { isOnLeaveInPeriod, leaveStartDate, leaveEndDate, leaveTypeName } = row.original;
        if (!isOnLeaveInPeriod) {
          return (
            <Text size="sm" c="dimmed">
              {t("leave:companyLedger.notOnLeave")}
            </Text>
          );
        }

        const range =
          leaveStartDate && leaveEndDate
            ? `${formatDateOnly(leaveStartDate, i18n.language, { day: "2-digit", month: "short" })} – ${formatDateOnly(leaveEndDate, i18n.language, { day: "2-digit", month: "short" })}`
            : undefined;
        return (
          <Tooltip label={[leaveTypeName, range].filter(Boolean).join(" · ")} disabled={!leaveTypeName && !range}>
            <Badge color="teal" variant="light">
              {t("leave:companyLedger.onLeave")}
            </Badge>
          </Tooltip>
        );
      },
    },
    {
      id: "annualLeaveBalance",
      accessorFn: (row) => row.annualLeaveBalance ?? Number.NEGATIVE_INFINITY,
      header: t("leave:companyLedger.columns.annualBalance"),
      cell: ({ row }) => (row.original.annualLeaveBalance == null ? "—" : row.original.annualLeaveBalance),
    },
    {
      id: "overtimeHoursYtd",
      accessorKey: "overtimeHoursYtd",
      header: t("leave:companyLedger.columns.overtimeHours"),
      cell: ({ row }) => row.original.overtimeHoursYtd,
    },
    {
      id: "overtimeLimitExceeded",
      accessorFn: (row) => row.overtimeLimitExceeded,
      header: t("leave:companyLedger.columns.overtimeLimit"),
      cell: ({ row }) =>
        row.original.overtimeLimitExceeded ? (
          <Badge color="red" variant="light">
            {t("leave:companyLedger.overtimeLimitExceeded")}
          </Badge>
        ) : (
          <Text size="sm" c="dimmed">
            {t("leave:companyLedger.overtimeLimitOk")}
          </Text>
        ),
    },
  ];

  const table = useDataTable({
    columns,
    data: rows,
    getRowId: (row) => row.employeeId,
    initialSorting: [{ id: "name", desc: false }],
    pageSize: 20,
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <NotebookText size={24} /> {t("leave:companyLedger.title")}
          </Title>
          <Text c="dimmed">{t("leave:companyLedger.subtitle")}</Text>
        </div>
        <Button leftSection={<Download size={16} />} variant="default" loading={exportMutation.isPending} onClick={() => exportMutation.mutate()}>
          {t("leave:companyLedger.export")}
        </Button>
      </Group>

      <Stack gap="sm">
        <Group gap="sm" wrap="wrap" align="center">
          <OrgUnitSelect value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("leave:companyLedger.allUnits")} className="w-64" />
          <Group gap={4} wrap="nowrap">
            <Tooltip label={t("leave:companyLedger.previousMonth")}>
              <ActionIcon
                variant="default"
                aria-label={t("leave:companyLedger.previousMonth")}
                onClick={() => setMonthStart((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))}
              >
                <ChevronLeft className="h-4 w-4" />
              </ActionIcon>
            </Tooltip>
            <Text fw={500} miw={140} ta="center">
              {formatDateOnly(toDateOnly(monthStart), i18n.language, { month: "long", year: "numeric" })}
            </Text>
            <Tooltip label={t("leave:companyLedger.nextMonth")}>
              <ActionIcon
                variant="default"
                aria-label={t("leave:companyLedger.nextMonth")}
                onClick={() => setMonthStart((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))}
              >
                <ChevronRight className="h-4 w-4" />
              </ActionIcon>
            </Tooltip>
          </Group>
          <Switch
            label={t("leave:companyLedger.includeTerminated")}
            checked={includeTerminated}
            onChange={(e) => setIncludeTerminated(e.currentTarget.checked)}
          />
        </Group>
        <Group gap="lg" wrap="wrap">
          <Checkbox label={t("leave:companyLedger.onlyOnLeave")} checked={onlyOnLeave} onChange={(e) => setOnlyOnLeave(e.currentTarget.checked)} />
          <Checkbox
            label={
              query.data ? t("leave:companyLedger.onlyOverLimit", { hours: query.data.overtimeLimitHours }) : t("leave:companyLedger.onlyOverLimitGeneric")
            }
            checked={onlyOverLimit}
            onChange={(e) => setOnlyOverLimit(e.currentTarget.checked)}
          />
        </Group>
      </Stack>

      {query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("leave:genericError"))}
        </Text>
      ) : (
        <DataTable table={table} isLoading={query.isLoading} isFetching={query.isFetching} emptyMessage={t("leave:companyLedger.empty")} />
      )}
    </div>
  );
}
