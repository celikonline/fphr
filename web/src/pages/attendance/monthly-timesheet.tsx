import { Fragment, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
} from "@mantine/core";
import {
  CalendarCheck,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Lock,
} from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { ConfirmModal } from "@/components/ui/confirm-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { formatDateOnly, formatDateTime, toDateOnly } from "@/lib/dates";
import {
  approveAllMonthlyTimesheets,
  approveMonthlyTimesheet,
  getMonthlyTimesheet,
  getTeamMonth,
  type MonthlyTimesheetRow,
  type TeamWeekDay,
  type TeamWeekRow,
} from "@/services/attendance.service";
import {
  dayChipTone,
  monthStartOf,
  pendingApprovalCount,
  shiftMonth,
  summarizeAnomalies,
  totalsOf,
  type DayChipTone,
} from "./monthly-timesheet-helpers";

const MONTHLY_KEY = ["attendance", "monthly-timesheet"] as const;
const TEAM_KEY_PREFIX = ["attendance", "team-timesheet"] as const;
const APPROVE_PERMISSION = "attendance.timesheet.approve";
const COLUMN_COUNT = 12;

const TONE_COLOR: Record<DayChipTone, string> = {
  empty: "gray",
  ok: "green",
  issue: "red",
  leaveConflict: "orange",
  settled: "yellow",
};

/**
 * "Aylık Puantaj" (11.3.6, S-11): satır = çalışan, kolonlar = ay toplamları (çalışılan gün/saat, eksik gün, fazla mesai,
 * izin günleri) + anomali özeti + onay durumu. Satır açılınca gün gün şerit (ekip ayı verisi, izinde çalışma anomalisi dahil)
 * görünür. "Onayla" / "Tümünü Onayla" `attendance.timesheet.approve` ister; izinsiz kullanıcıda butonlar hiç çıkmaz.
 */
export default function MonthlyTimesheetPage() {
  const canView = usePermission("attendance.timesheet.view");
  if (!canView) return <NoAccess />;
  return <MonthlyTimesheet />;
}

function MonthlyTimesheet() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const canApprove = usePermission(APPROVE_PERMISSION);
  const [monthStart, setMonthStart] = useState(() => monthStartOf(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const [confirmAll, setConfirmAll] = useState(false);

  const monthIso = toDateOnly(monthStart);
  const monthLabel = formatDateOnly(monthIso, i18n.language, { month: "long", year: "numeric" });
  const numberFormat = useMemo(
    () => new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }),
    [i18n.language]
  );

  const monthly = useQuery({
    queryKey: [...MONTHLY_KEY, monthIso, orgUnitId],
    queryFn: () => getMonthlyTimesheet(monthIso, orgUnitId),
  });
  // Day strip data shares the team-month cache; a failure there only hides the strips, never the totals.
  const dayGrid = useQuery({
    queryKey: [...TEAM_KEY_PREFIX, "month", monthIso, orgUnitId],
    queryFn: () => getTeamMonth(monthIso, orgUnitId),
  });

  const rows = useMemo(
    () => [...(monthly.data ?? [])].sort((a, b) => a.employeeName.localeCompare(b.employeeName)),
    [monthly.data]
  );
  const daysByEmployee = useMemo(
    () => new Map((dayGrid.data ?? []).map((row: TeamWeekRow) => [row.employeeId, row.days])),
    [dayGrid.data]
  );
  const totals = useMemo(() => totalsOf(rows), [rows]);
  const pending = pendingApprovalCount(rows);

  function reportError(error: unknown) {
    const message =
      getApiErrorStatus(error) === 403
        ? t("attendance:monthlyTimesheet.forbidden")
        : getApiErrorMessage(error, t("attendance:genericError"));
    toast({ variant: "destructive", description: message });
  }

  function refreshAll() {
    void queryClient.invalidateQueries({ queryKey: MONTHLY_KEY });
    void queryClient.invalidateQueries({ queryKey: TEAM_KEY_PREFIX });
  }

  const approveOne = useMutation({
    mutationFn: (employeeId: string) => approveMonthlyTimesheet(employeeId, monthIso),
    onSuccess: () => {
      toast({ variant: "success", description: t("attendance:monthlyTimesheet.approved") });
      refreshAll();
    },
    onError: reportError,
  });

  const approveAll = useMutation({
    mutationFn: () => approveAllMonthlyTimesheets(monthIso, orgUnitId),
    onSuccess: (count) => {
      toast({
        variant: "success",
        description:
          count > 0
            ? t("attendance:monthlyTimesheet.approveAll.done", { count })
            : t("attendance:monthlyTimesheet.approveAll.nothing"),
      });
      refreshAll();
    },
    onError: reportError,
  });

  function toggle(employeeId: string) {
    setExpanded((current) => {
      const next = new Set(current);
      if (!next.delete(employeeId)) next.add(employeeId);
      return next;
    });
  }

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarCheck size={24} /> {t("attendance:monthlyTimesheet.title")}
          </Title>
          <Text c="dimmed">{t("attendance:monthlyTimesheet.subtitle")}</Text>
        </div>
        {canApprove && (
          <Button
            onClick={() => setConfirmAll(true)}
            disabled={monthly.isLoading || pending === 0}
            loading={approveAll.isPending}
          >
            {t("attendance:monthlyTimesheet.approveAll.button")}
          </Button>
        )}
      </Group>

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <OrgUnitSelect
          value={orgUnitId}
          onChange={setOrgUnitId}
          emptyLabel={t("attendance:teamTimesheet.allUnits")}
          className="w-64"
        />
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance:previousMonth")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:previousMonth")}
              onClick={() => setMonthStart((d) => shiftMonth(d, -1))}
            >
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} miw={180} ta="center">
            {monthLabel}
          </Text>
          <Tooltip label={t("attendance:nextMonth")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:nextMonth")}
              onClick={() => setMonthStart((d) => shiftMonth(d, 1))}
            >
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {!canApprove && (
        <Text size="sm" c="dimmed">
          {t("attendance:monthlyTimesheet.readOnly")}
        </Text>
      )}

      {monthly.isLoading ? (
        <Stack gap="xs">
          <Skeleton h={40} />
          <Skeleton h={40} />
          <Skeleton h={40} />
        </Stack>
      ) : monthly.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(monthly.error, t("attendance:genericError"))}
        </Text>
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("attendance:monthlyTimesheet.empty")}
        </Text>
      ) : (
        <div className="overflow-x-auto">
          <Table withTableBorder withColumnBorders verticalSpacing="xs" miw={1100}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={40} />
                <Table.Th>{t("attendance:teamTimesheet.employee")}</Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.workedDays")}</Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.workedHours")}</Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.missingDays")}</Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.overtimeHours")}</Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.paidLeaveDays")}</Table.Th>
                <Table.Th ta="right">
                  {t("attendance:monthlyTimesheet.col.unpaidLeaveDays")}
                </Table.Th>
                <Table.Th ta="right">{t("attendance:monthlyTimesheet.col.sickLeaveDays")}</Table.Th>
                <Table.Th>{t("attendance:monthlyTimesheet.col.anomalies")}</Table.Th>
                <Table.Th>{t("attendance:monthlyTimesheet.col.status")}</Table.Th>
                <Table.Th>
                  {canApprove ? t("attendance:monthlyTimesheet.col.action") : null}
                </Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => (
                <Fragment key={row.employeeId}>
                  <MonthlyRow
                    row={row}
                    days={daysByEmployee.get(row.employeeId)}
                    dayGridReady={dayGrid.isSuccess}
                    isExpanded={expanded.has(row.employeeId)}
                    onToggle={() => toggle(row.employeeId)}
                    canApprove={canApprove}
                    approving={approveOne.isPending && approveOne.variables === row.employeeId}
                    onApprove={() => approveOne.mutate(row.employeeId)}
                    format={(value) => numberFormat.format(value)}
                  />
                  {expanded.has(row.employeeId) && (
                    <Table.Tr>
                      <Table.Td colSpan={COLUMN_COUNT}>
                        <DayStrip
                          days={daysByEmployee.get(row.employeeId)}
                          loading={dayGrid.isLoading}
                          failed={dayGrid.isError}
                        />
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Fragment>
              ))}
            </Table.Tbody>
            <Table.Tfoot>
              <Table.Tr>
                <Table.Td />
                <Table.Td fw={600}>{t("attendance:monthlyTimesheet.totals")}</Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.workedDays)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.workedHours)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.missingDays)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.overtimeHours)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.paidLeaveDays)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.unpaidLeaveDays)}
                </Table.Td>
                <Table.Td ta="right" fw={600}>
                  {numberFormat.format(totals.sickLeaveDays)}
                </Table.Td>
                <Table.Td colSpan={3} c="dimmed">
                  {t("attendance:monthlyTimesheet.pendingCount", { pending, total: rows.length })}
                </Table.Td>
              </Table.Tr>
            </Table.Tfoot>
          </Table>
        </div>
      )}

      {confirmAll && (
        <ConfirmModal
          opened
          onClose={() => setConfirmAll(false)}
          title={t("attendance:monthlyTimesheet.approveAll.confirmTitle")}
          message={t("attendance:monthlyTimesheet.approveAll.confirmMessage", {
            month: monthLabel,
            count: pending,
          })}
          confirmLabel={t("attendance:monthlyTimesheet.approveAll.button")}
          onConfirm={async () => {
            // Errors are toasted by the mutation's onError; swallowing here closes the dialog instead of leaving an unhandled rejection.
            await approveAll.mutateAsync().catch(() => undefined);
          }}
        />
      )}
    </div>
  );
}

interface MonthlyRowProps {
  row: MonthlyTimesheetRow;
  days: TeamWeekDay[] | undefined;
  dayGridReady: boolean;
  isExpanded: boolean;
  onToggle: () => void;
  canApprove: boolean;
  approving: boolean;
  onApprove: () => void;
  format: (value: number) => string;
}

function MonthlyRow({
  row,
  days,
  dayGridReady,
  isExpanded,
  onToggle,
  canApprove,
  approving,
  onApprove,
  format,
}: MonthlyRowProps) {
  const { t, i18n } = useTranslation(["attendance"]);
  const anomalies = summarizeAnomalies(days);
  const name = row.employeeName || "—";

  return (
    <Table.Tr>
      <Table.Td p={4}>
        <ActionIcon
          variant="subtle"
          aria-label={t("attendance:monthlyTimesheet.toggleDays", { name })}
          aria-expanded={isExpanded}
          onClick={onToggle}
        >
          {isExpanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
        </ActionIcon>
      </Table.Td>
      <Table.Td fw={500}>{name}</Table.Td>
      <Table.Td ta="right">{format(row.workedDays)}</Table.Td>
      <Table.Td ta="right">{format(row.workedHours)}</Table.Td>
      <Table.Td ta="right" c={row.missingDays > 0 ? "red" : undefined}>
        {format(row.missingDays)}
      </Table.Td>
      <Table.Td ta="right">{format(row.overtimeHours)}</Table.Td>
      <Table.Td ta="right">{format(row.paidLeaveDays)}</Table.Td>
      <Table.Td ta="right">{format(row.unpaidLeaveDays)}</Table.Td>
      <Table.Td ta="right">{format(row.sickLeaveDays)}</Table.Td>
      <Table.Td>
        {!dayGridReady ? (
          <Text size="sm" c="dimmed">
            —
          </Text>
        ) : anomalies.unresolvedDays === 0 ? (
          <Badge size="sm" color="green" variant="light">
            {t("attendance:monthlyTimesheet.noAnomalies")}
          </Badge>
        ) : (
          <Group gap={4} wrap="wrap">
            <Badge size="sm" color="red" variant="light">
              {t("attendance:monthlyTimesheet.anomalyDays", { count: anomalies.unresolvedDays })}
            </Badge>
            {anomalies.workedWhileOnLeaveDays > 0 && (
              <Badge size="sm" color="orange" variant="filled">
                {t("attendance:anomaly.workedWhileOnLeave")}: {anomalies.workedWhileOnLeaveDays}
              </Badge>
            )}
          </Group>
        )}
      </Table.Td>
      <Table.Td>
        <Group gap={4} wrap="wrap">
          {row.isApproved ? (
            <Tooltip
              label={
                row.approvedAt
                  ? t("attendance:monthlyTimesheet.approvedAt", {
                      date: formatDateTime(row.approvedAt, i18n.language),
                    })
                  : t("attendance:monthlyTimesheet.approvedState")
              }
            >
              <Badge size="sm" color="green">
                {t("attendance:monthlyTimesheet.approvedState")}
              </Badge>
            </Tooltip>
          ) : (
            <Badge size="sm" color="gray" variant="light">
              {t("attendance:monthlyTimesheet.pendingState")}
            </Badge>
          )}
          {row.isPeriodLocked && (
            <Tooltip label={t("attendance:monthlyTimesheet.locked")}>
              <Badge
                size="sm"
                color="steel"
                variant="outline"
                leftSection={<Lock className="h-3 w-3" />}
              >
                {" "}
              </Badge>
            </Tooltip>
          )}
        </Group>
      </Table.Td>
      <Table.Td>
        {canApprove && (
          <Button
            size="xs"
            variant={row.isApproved ? "default" : "filled"}
            onClick={onApprove}
            loading={approving}
            aria-label={t("attendance:monthlyTimesheet.approveRow", { name })}
          >
            {row.isApproved
              ? t("attendance:monthlyTimesheet.reapprove")
              : t("attendance:monthlyTimesheet.approve")}
          </Button>
        )}
      </Table.Td>
    </Table.Tr>
  );
}

function DayStrip({
  days,
  loading,
  failed,
}: {
  days: TeamWeekDay[] | undefined;
  loading: boolean;
  failed: boolean;
}) {
  const { t, i18n } = useTranslation(["attendance"]);
  if (loading) return <Skeleton h={28} />;
  if (failed || !days) {
    return (
      <Text size="sm" c="dimmed">
        {t("attendance:monthlyTimesheet.daysUnavailable")}
      </Text>
    );
  }

  return (
    <Group gap={4} wrap="wrap" data-testid="day-strip">
      {days.map((day) => {
        const hours =
          day.workedMinutes > 0
            ? `${Math.round((day.workedMinutes / 60) * 100) / 100} ${t("attendance:monthlyTimesheet.hourUnit")}`
            : "—";
        const flags = day.anomalies.map((flag) => t(`attendance:anomaly.${flag}`));
        const label = [
          formatDateOnly(day.date, i18n.language, { day: "2-digit", month: "short" }),
          hours,
          ...flags,
        ].join(" · ");
        return (
          <Tooltip key={day.date} label={label} multiline maw={260}>
            <Badge
              size="lg"
              radius="sm"
              variant={
                day.anomalies.includes("workedWhileOnLeave") && day.status === "unresolved"
                  ? "filled"
                  : "light"
              }
              color={TONE_COLOR[dayChipTone(day)]}
              aria-label={label}
              data-tone={dayChipTone(day)}
              styles={{ root: { minWidth: 34, paddingInline: 6 } }}
            >
              {day.date.slice(8, 10)}
            </Badge>
          </Tooltip>
        );
      })}
    </Group>
  );
}
