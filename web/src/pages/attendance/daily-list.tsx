import { useTranslation } from "react-i18next";
import { Navigate, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, ClipboardList, FileUp, LifeBuoy } from "lucide-react";
import { ActionIcon, Alert, Badge, Button, Group, Loader, Pagination, Paper, SimpleGrid, Tabs, Text, TextInput, Title, Tooltip } from "@mantine/core";
import { Link } from "react-router";
import DailyGrid from "@/components/attendance/daily-grid";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import NoAccess from "@/components/no-access";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { usePermission } from "@/components/permission-guard";
import { addDaysToDateOnly, toDateOnly } from "@/lib/dates";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  addManualPunch,
  deletePunch,
  getDailyCounts,
  listDaily,
  type DailyStatus,
} from "@/services/attendance-daily.service";

const STATUSES: readonly DailyStatus[] = [
  "arrived", "late", "earlyLeave", "absent", "onLeave", "overtime", "undertime", "incalculable",
];
const DAILY_KEY = ["attendance-daily"] as const;
const DAILY_PATH = "/app/attendance/daily";

/** Günlük puantaj çalışma alanı (/app/attendance/daily/:status?date=): filtreler, durum özeti, hareket detayı ve manuel düzeltmeler. */
export default function DailyListPage() {
  const { t } = useTranslation(["attendance-daily"]);
  const { status: statusParam } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canView = usePermission("attendance.daily.view");
  const canManage = usePermission("attendance.punch.manage");
  const canImport = usePermission("attendance.timesheet.import");

  const status = statusParam === "all" ? "all" : STATUSES.find((s) => s === statusParam);
  const selectedStatus = status === "all" ? undefined : status;
  const date = searchParams.get("date") || toDateOnly(new Date());
  const orgUnitId = searchParams.get("orgUnitId");
  const page = Number(searchParams.get("page")) || 1;
  const unit = orgUnitId ?? undefined;

  const list = useQuery({
    queryKey: [...DAILY_KEY, "list", date, status, unit, page],
    queryFn: () => listDaily(date, selectedStatus, unit, page),
    enabled: canView && status !== undefined,
  });
  const counts = useQuery({
    queryKey: [...DAILY_KEY, "counts", date, unit],
    queryFn: () => getDailyCounts(date, unit),
    enabled: canView,
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: DAILY_KEY });
  const addMutation = useMutation({ mutationFn: addManualPunch, onSuccess: refresh });
  const deleteMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => deletePunch(id, reason),
    onSuccess: refresh,
  });

  if (!canView) return <NoAccess />;
  if (!status) return <Navigate to={`${DAILY_PATH}/all`} replace />;

  function updateParams(changes: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    setSearchParams(next);
  }
  const changeDate = (value: string) => updateParams({ date: value, page: null });

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <ClipboardList size={24} /> {t("attendance-daily:dailyList.title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>{t("attendance-daily:dailyList.subtitle")}</Text>
      </div>

      <Group justify="space-between" align="flex-end" wrap="wrap" gap="sm">
        <OrgUnitSelect
          value={orgUnitId}
          onChange={(v) => updateParams({ orgUnitId: v, page: null })}
          emptyLabel={t("attendance-daily:dailyList.allUnits")}
          className="w-64"
        />
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance-daily:dailyList.previousDay")}>
            <ActionIcon variant="default" aria-label={t("attendance-daily:dailyList.previousDay")} onClick={() => changeDate(addDaysToDateOnly(date, -1))}>
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <TextInput
            type="date"
            aria-label={t("attendance-daily:dailyList.date")}
            value={date}
            onChange={(e) => e.currentTarget.value && changeDate(e.currentTarget.value)}
          />
          <Tooltip label={t("attendance-daily:dailyList.nextDay")}>
            <ActionIcon variant="default" aria-label={t("attendance-daily:dailyList.nextDay")} onClick={() => changeDate(addDaysToDateOnly(date, 1))}>
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }} spacing="sm">
        {([
          ["arrived", counts.data?.arrived],
          ["late", counts.data?.late],
          ["earlyLeave", counts.data?.earlyLeave],
          ["absent", counts.data?.absent],
          ["onLeave", counts.data?.onLeave],
          ["overtime", counts.data?.overtime],
        ] as const).map(([key, value]) => (
          <Paper key={key} withBorder radius="md" p="sm">
            <Text size="xs" c="dimmed">{t(`attendance-daily:dailyList.summary.${key}`)}</Text>
            <Text fw={700} size="lg">{value ?? "—"}</Text>
          </Paper>
        ))}
      </SimpleGrid>

      <Tabs
        value={status}
        onChange={(value) => {
          if (!value) return;
          const next = new URLSearchParams(searchParams);
          next.delete("page");
          navigate({ pathname: `${DAILY_PATH}/${value}`, search: next.toString() });
        }}
      >
        <Tabs.List>
          <Tabs.Tab value="all" rightSection={counts.data ? <Badge size="sm" variant="light" circle>{list.data?.totalCount ?? "—"}</Badge> : null}>
            {t("attendance-daily:dailyList.status.all")}
          </Tabs.Tab>
          {STATUSES.map((s) => (
            <Tabs.Tab
              key={s}
              value={s}
              rightSection={
                counts.data?.[s] !== undefined ? <Badge size="sm" variant="light" circle>{counts.data[s]}</Badge> : null
              }
            >
              {t(`attendance-daily:dailyList.status.${s}`)}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      {list.isLoading && <Loader size="sm" />}
      {list.isError && (
        <Alert color="red">{getApiErrorMessage(list.error, t("attendance-daily:dailyList.loadError"))}</Alert>
      )}
      {list.data && list.data.items.length === 0 && (
        <AttendanceEmptyState
          title={t("attendance-daily:dailyList.empty")}
          description={t("attendance-daily:dailyList.emptyGuide")}
          action={canImport ? (
            <Button component={Link} to="/app/attendance/device-logs/import" leftSection={<FileUp size={16} />}>
              {t("attendance-daily:dailyList.importRecords")}
            </Button>
          ) : (
            <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
              {t("attendance-daily:dailyList.contactSupport")}
            </Button>
          )}
        />
      )}
      {list.data && list.data.items.length > 0 && (
        <>
          <DailyGrid
            rows={list.data.items}
            canManage={canManage}
            onAddPunch={(input) => addMutation.mutateAsync(input)}
            onDeletePunch={(id, reason) => deleteMutation.mutateAsync({ id, reason })}
          />
          {list.data.totalPages > 1 && (
            <Pagination value={page} total={list.data.totalPages} onChange={(p) => updateParams({ page: String(p) })} />
          )}
        </>
      )}
    </div>
  );
}
