import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  SegmentedControl,
  Skeleton,
  Stack,
  Switch,
  Text,
  Textarea,
  Title,
  Tooltip,
} from "@mantine/core";
import { CalendarClock, ChevronLeft, ChevronRight, LayoutTemplate, Lock, LockOpen, Moon, Send, Trash2, X } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import ShiftCalendarGrid, { cellKey, cellRange, isNonWorkDay, type CellRef } from "@/components/attendance/shift-calendar-grid";
import ShiftTemplateDialog from "@/components/attendance/shift-template-dialog";
import ShiftPalette, { type PalettePayload } from "@/components/attendance/shift-palette";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime, toDateOnly } from "@/lib/dates";
import { listEmployees } from "@/services/employees.service";
import {
  getShiftPlanLockStatus,
  getUnpublishedShiftCount,
  listShiftAssignments,
  lockShiftPeriod,
  publishShiftAssignments,
  unlockShiftPeriod,
  WEEK_DAYS_MONDAY_FIRST,
} from "@/services/attendance.service";
import { listShiftDefinitions } from "@/services/attendance-shift-definitions.service";
import {
  bulkAssignShifts,
  bulkClearShifts,
  getShiftPlanTotals,
  listUnassignedEmployeeIds,
  type BulkShiftResult,
  type CalendarShiftAssignment,
  type ShiftDayTotal,
} from "@/services/attendance-shift-calendar.service";

const KEY_PREFIX = ["attendance", "shift-plan"] as const;
/** Fetched once per org-unit filter as both the row list (names) and the name lookup for the grid. */
const EMPLOYEE_PAGE_SIZE = 200;

/** Monday of the calendar week that contains `date` (local time, no UTC shift; same as `team-timesheet.tsx`). */
function mondayOf(date: Date): Date {
  const day = date.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  monday.setDate(monday.getDate() + diff);
  return monday;
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
function formatTime(value: string): string {
  return value.slice(0, 5);
}

/**
 * Yönetici vardiya planı (11.3.6): satır = çalışan, kolon = gün (Pzt–Paz); salt görüntüleme + yayınla + dönem
 * kilit bandı. Vardiya girme/düzenleme ve "Tekrarla" ayrı bir turda planlı.
 */
export default function ShiftPlanPage() {
  const canView = usePermission("attendance.shift.view");
  if (!canView) return <NoAccess />;
  return <ShiftPlan />;
}

function ShiftPlan() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const canPublish = usePermission("attendance.shift.publish");
  const canLock = usePermission("attendance.shift.lock");
  const canUnlock = usePermission("attendance.shift.unlock");
  const canPlan = usePermission("attendance.shift.plan");

  const [weekStart, setWeekStart] = useState(() => mondayOf(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [selectedDayIndex, setSelectedDayIndex] = useState(() => {
    const day = new Date().getDay(); // 0 = Pazar
    return day === 0 ? 6 : day - 1; // Pazartesi-ilk index
  });
  const [publishing, setPublishing] = useState(false);
  const [locking, setLocking] = useState(false);
  const [unlocking, setUnlocking] = useState(false);
  const [unlockReason, setUnlockReason] = useState("");
  const [unlockError, setUnlockError] = useState<string | null>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(() => new Set());
  const [selectionAnchor, setSelectionAnchor] = useState<{ employeeId: string; date: string } | null>(null);
  const [armedPayload, setArmedPayload] = useState<PalettePayload | null>(null);
  const [clearing, setClearing] = useState(false);
  const [unassignedOnly, setUnassignedOnly] = useState(false);
  const [templatesOpen, setTemplatesOpen] = useState(false);

  const weekStartIso = toDateOnly(weekStart);
  const weekDates = useMemo(
    () => WEEK_DAYS_MONDAY_FIRST.map((_, i) => toDateOnly(new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() + i))),
    [weekStart],
  );
  const weekEnd = weekDates[weekDates.length - 1];
  const selectedDate = weekDates[selectedDayIndex];
  /** "Haftanın ayı" (TALIMAT): kilit bandı, haftanın başladığı ayın dönemi üzerinden gösterilir. */
  const periodMonth = toDateOnly(new Date(weekStart.getFullYear(), weekStart.getMonth(), 1));

  // Grid çalışan listesi + ad eşlemesi tek kaynaktan: `listShiftAssignments` yalnız `employeeId` döner.
  const employeesQuery = useQuery({
    queryKey: [...KEY_PREFIX, "employees", orgUnitId],
    queryFn: () =>
      listEmployees({ page: 1, pageSize: EMPLOYEE_PAGE_SIZE, status: "Active", orgUnitId: orgUnitId ?? undefined, includeSubUnits: true, sort: "displayName" }),
  });
  const employees = useMemo(
    () => [...(employeesQuery.data?.items ?? [])].sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [employeesQuery.data],
  );
  const employeeIds = useMemo(() => employees.map((e) => e.id), [employees]);

  const assignmentsQuery = useQuery({
    queryKey: [...KEY_PREFIX, weekStartIso, orgUnitId],
    queryFn: () => listShiftAssignments({ from: weekStartIso, to: weekEnd, employeeIds }),
    enabled: employeesQuery.isSuccess,
  });

  const unpublishedQuery = useQuery({
    queryKey: [...KEY_PREFIX, "unpublished", weekStartIso],
    queryFn: () => getUnpublishedShiftCount({ from: weekStartIso, to: weekEnd }),
  });

  /** A week can span two months; each month has its own lock. */
  const weekEndMonth = `${weekEnd.slice(0, 7)}-01`;
  const endMonthDiffers = weekEndMonth !== periodMonth;
  const endLockQuery = useQuery({
    queryKey: [...KEY_PREFIX, "lock", weekEndMonth],
    queryFn: () => getShiftPlanLockStatus(weekEndMonth),
    enabled: endMonthDiffers,
  });

  const lockQuery = useQuery({
    queryKey: [...KEY_PREFIX, "lock", periodMonth],
    queryFn: () => getShiftPlanLockStatus(periodMonth),
  });

  const definitionsQuery = useQuery({
    queryKey: [...KEY_PREFIX, "definitions"],
    queryFn: () => listShiftDefinitions(),
    enabled: canPlan,
  });
  const definitionNames = useMemo(
    () => new Map((definitionsQuery.data ?? []).flatMap((d) => (d.id ? [[d.id, d.name] as const] : []))),
    [definitionsQuery.data],
  );

  const totalsQuery = useQuery({
    queryKey: [...KEY_PREFIX, "totals", weekStartIso, orgUnitId],
    queryFn: () => getShiftPlanTotals({ from: weekStartIso, to: weekEnd, employeeIds }),
    enabled: employeesQuery.isSuccess,
  });
  const totalsByDate = useMemo(
    () => new Map<string, ShiftDayTotal>((totalsQuery.data?.byDate ?? []).map((total) => [total.date, total])),
    [totalsQuery.data],
  );

  const unassignedQuery = useQuery({
    queryKey: [...KEY_PREFIX, "unassigned", weekStartIso, orgUnitId],
    queryFn: () => listUnassignedEmployeeIds({ from: weekStartIso, to: weekEnd, employeeIds }),
    enabled: unassignedOnly && employeesQuery.isSuccess,
  });
  const visibleEmployees = useMemo(() => {
    if (!unassignedOnly || !unassignedQuery.data) return employees;
    const unassigned = new Set(unassignedQuery.data);
    return employees.filter((e) => unassigned.has(e.id));
  }, [employees, unassignedOnly, unassignedQuery.data]);

  const assignmentsByEmployee = useMemo(() => {
    const index = new Map<string, Map<string, CalendarShiftAssignment>>();
    for (const assignment of (assignmentsQuery.data ?? []) as CalendarShiftAssignment[]) {
      let byDate = index.get(assignment.employeeId);
      if (!byDate) {
        byDate = new Map();
        index.set(assignment.employeeId, byDate);
      }
      if (!byDate.has(assignment.date)) byDate.set(assignment.date, assignment);
    }
    return index;
  }, [assignmentsQuery.data]);

  const invalidateAll = () => queryClient.invalidateQueries({ queryKey: KEY_PREFIX });

  const resetSelection = () => {
    setSelectedKeys(new Set());
    setSelectionAnchor(null);
  };

  const selectedCells = useMemo(() => {
    const employeeSet = new Set<string>();
    const dateSet = new Set<string>();
    for (const key of selectedKeys) {
      const [employeeId, date] = key.split("|");
      employeeSet.add(employeeId);
      dateSet.add(date);
    }
    return {
      employeeIds: visibleEmployees.map((e) => e.id).filter((id) => employeeSet.has(id)),
      dates: weekDates.filter((d) => dateSet.has(d)),
    };
  }, [selectedKeys, visibleEmployees, weekDates]);

  const reportBulkResult = (result: BulkShiftResult) => {
    toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftPlan.bulkDone", { count: result.affectedCount }) });
    if (result.skipped.length > 0) {
      toast({ variant: "default", description: t("attendance:shiftPlan.bulkSkipped", { count: result.skipped.length }) });
    }
  };

  const assignMutation = useMutation({
    mutationFn: ({ employeeIds: ids, dates, payload }: { employeeIds: string[]; dates: string[]; payload: PalettePayload }) =>
      bulkAssignShifts(
        payload.kind === "shift"
          ? { employeeIds: ids, dates, shiftDefinitionId: payload.shiftDefinitionId, dayType: "work" }
          : { employeeIds: ids, dates, dayType: payload.dayType },
      ),
    onSuccess: async (result) => {
      await invalidateAll();
      reportBulkResult(result);
      resetSelection();
      setArmedPayload(null);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("attendance:genericError")) }),
  });

  const clearMutation = useMutation({
    mutationFn: () => bulkClearShifts({ employeeIds: selectedCells.employeeIds, dates: selectedCells.dates }),
    onSuccess: async (result) => {
      await invalidateAll();
      reportBulkResult(result);
      resetSelection();
      setClearing(false);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("attendance:genericError")) }),
  });

  const publishMutation = useMutation({
    mutationFn: () => {
      const draftIds = (assignmentsQuery.data ?? []).filter((a) => a.status === "draft").map((a) => a.id);
      return publishShiftAssignments(draftIds);
    },
    onSuccess: async () => {
      await invalidateAll();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftPlan.published") });
      setPublishing(false);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("attendance:genericError")) }),
  });

  const lockMutation = useMutation({
    mutationFn: () => lockShiftPeriod(periodMonth),
    onSuccess: async () => {
      await invalidateAll();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftPlan.locked") });
      setLocking(false);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("attendance:genericError")) }),
  });

  const unlockMutation = useMutation({
    mutationFn: () => unlockShiftPeriod(periodMonth, unlockReason.trim()),
    onSuccess: async () => {
      await invalidateAll();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftPlan.unlocked") });
      setUnlocking(false);
      setUnlockReason("");
    },
    onError: (err) => setUnlockError(getApiErrorMessage(err, t("attendance:genericError"))),
  });

  const isLoading = employeesQuery.isLoading || assignmentsQuery.isLoading;
  const isError = employeesQuery.isError || assignmentsQuery.isError;
  const isLocked = lockQuery.data?.isLocked ?? false;
  const draftCount = (assignmentsQuery.data ?? []).filter((a) => a.status === "draft").length;
  const isDateLocked = (date: string) => {
    const month = `${date.slice(0, 7)}-01`;
    if (month === periodMonth) return lockQuery.data?.isLocked ?? false;
    return month === weekEndMonth ? (endLockQuery.data?.isLocked ?? false) : false;
  };
  const lockedDayCount = weekDates.filter(isDateLocked).length;
  const canEditCells = canPlan && lockedDayCount < weekDates.length;

  const handleCellClick = (employeeId: string, date: string, extend: boolean) => {
    if (extend && selectionAnchor) {
      const range = cellRange(visibleEmployees.map((e) => e.id), weekDates, selectionAnchor, { employeeId, date });
      if (range) {
        setSelectedKeys(new Set(range.employeeIds.flatMap((id) => range.dates.map((d) => cellKey(id, d)))));
        return;
      }
    }
    const key = cellKey(employeeId, date);
    const alreadyOnlySelected = selectedKeys.size === 1 && selectedKeys.has(key);
    setSelectedKeys(alreadyOnlySelected ? new Set() : new Set([key]));
    setSelectionAnchor(alreadyOnlySelected ? null : { employeeId, date });
  };

  /** Drop on a selected cell applies to the whole selection; elsewhere only to the target cell. */
  const handleCellDrop = (employeeId: string, date: string, payload: PalettePayload) => {
    const onSelection = selectedKeys.has(cellKey(employeeId, date));
    assignMutation.mutate({
      employeeIds: onSelection ? selectedCells.employeeIds : [employeeId],
      dates: onSelection ? selectedCells.dates : [date],
      payload,
    });
  };

  /** With a selection the chip applies at once; otherwise it is armed for a keyboard Enter on a cell. */
  const handlePaletteClick = (payload: PalettePayload) => {
    if (selectedKeys.size === 0) {
      setArmedPayload((current) => (JSON.stringify(current) === JSON.stringify(payload) ? null : payload));
      return;
    }
    assignMutation.mutate({ ...selectedCells, payload });
  };

  const handleCellActivate = (employeeId: string, date: string) => {
    if (armedPayload) return handleCellDrop(employeeId, date, armedPayload);
    handleCellClick(employeeId, date, false);
  };

  const handleCellExtend = (from: CellRef, to: CellRef) => {
    const anchor = selectionAnchor ?? from;
    const range = cellRange(visibleEmployees.map((e) => e.id), weekDates, anchor, to);
    if (!range) return;
    setSelectionAnchor(anchor);
    setSelectedKeys(new Set(range.employeeIds.flatMap((id) => range.dates.map((d) => cellKey(id, d)))));
  };

  const changeWeek = (days: number) => {
    resetSelection();
    setWeekStart((d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + days));
  };

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarClock size={24} /> {t("attendance:shiftPlan.title")}
          </Title>
          <Text c="dimmed">{t("attendance:shiftPlan.subtitle")}</Text>
        </div>
      </Group>

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <OrgUnitSelect value={orgUnitId} onChange={(value) => { resetSelection(); setOrgUnitId(value); }} emptyLabel={t("attendance:shiftPlan.allUnits")} className="w-64" />
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance:shiftPlan.previousWeek")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:shiftPlan.previousWeek")}
              onClick={() => changeWeek(-7)}
            >
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} miw={200} ta="center">
            {formatDateOnly(weekStartIso, i18n.language, { day: "2-digit", month: "short" })} – {formatDateOnly(weekEnd, i18n.language, { day: "2-digit", month: "short", year: "numeric" })}
          </Text>
          <Tooltip label={t("attendance:shiftPlan.nextWeek")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:shiftPlan.nextWeek")}
              onClick={() => changeWeek(7)}
            >
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        {isLocked ? (
          <Badge size="lg" variant="light" color="red" leftSection={<Lock size={14} />}>
            {t("attendance:shiftPlan.lockedBadge", { date: lockQuery.data?.lockedAt ? formatDateTime(lockQuery.data.lockedAt, i18n.language) : "—" })}
          </Badge>
        ) : lockQuery.data?.scheduledLockDate ? (
          <Text size="sm" c="dimmed">
            {t("attendance:shiftPlan.scheduledLock", { date: formatDateOnly(lockQuery.data.scheduledLockDate, i18n.language, { day: "2-digit", month: "2-digit", year: "numeric" }) })}
          </Text>
        ) : (
          <span />
        )}

        <Group gap="xs" wrap="wrap">
          {canPlan && (
            <Button variant="default" size="xs" leftSection={<LayoutTemplate size={14} />} onClick={() => setTemplatesOpen(true)}>
              {t("attendance:shiftTemplates.title")}
            </Button>
          )}
          {isLocked && canUnlock && (
            <Button variant="default" size="xs" leftSection={<LockOpen size={14} />} onClick={() => setUnlocking(true)}>
              {t("attendance:shiftPlan.unlock")}
            </Button>
          )}
          {!isLocked && canLock && (
            <Button variant="default" size="xs" leftSection={<Lock size={14} />} onClick={() => setLocking(true)}>
              {t("attendance:shiftPlan.lock")}
            </Button>
          )}
          {canPublish && (unpublishedQuery.data ?? 0) > 0 && (
            <Group gap="xs" wrap="nowrap">
              <Text size="sm" c="dimmed">
                {t("attendance:shiftPlan.unpublishedCount", { count: unpublishedQuery.data ?? 0 })}
              </Text>
              <Button size="xs" leftSection={<Send size={14} />} disabled={isLocked || draftCount === 0} onClick={() => setPublishing(true)}>
                {t("attendance:shiftPlan.publish")}
              </Button>
            </Group>
          )}
        </Group>
      </Group>

      {canPlan && (
        <>
          <ShiftPalette definitions={definitionsQuery.data ?? []} disabled={!canEditCells || assignMutation.isPending} armed={armedPayload} onPick={handlePaletteClick} />
          {lockedDayCount > 0 && (
            <Text size="sm" c="dimmed">
              {t("attendance:shiftPlan.lockedEditHint")}
            </Text>
          )}
        </>
      )}

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <Switch
          label={t("attendance:shiftPlan.unassignedOnly")}
          checked={unassignedOnly}
          onChange={(e) => {
            resetSelection();
            setUnassignedOnly(e.currentTarget.checked);
          }}
        />
        {selectedKeys.size > 0 && (
          <Group gap="xs" wrap="wrap">
            <Text size="sm">{t("attendance:shiftPlan.selectedCells", { count: selectedKeys.size })}</Text>
            <Button size="xs" variant="default" leftSection={<X size={14} />} onClick={resetSelection}>
              {t("attendance:shiftPlan.clearSelection")}
            </Button>
            <Button size="xs" color="red" variant="light" leftSection={<Trash2 size={14} />} onClick={() => setClearing(true)}>
              {t("attendance:shiftPlan.clearShifts")}
            </Button>
          </Group>
        )}
      </Group>

      {isLoading ? (
        <Stack gap="xs">
          <Skeleton h={40} />
          <Skeleton h={40} />
          <Skeleton h={40} />
        </Stack>
      ) : isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(employeesQuery.error ?? assignmentsQuery.error, t("attendance:genericError"))}
        </Text>
      ) : visibleEmployees.length === 0 ? (
        <Text size="sm" c="dimmed">
          {unassignedOnly && employees.length > 0 ? t("attendance:shiftPlan.unassignedEmpty") : t("attendance:shiftPlan.empty")}
        </Text>
      ) : (
        <>
          {/* Dar viewport (≤~576px): 7 kolonlu ızgara sığmaz; gün seçici + tek günlük çalışan kart listesi. */}
          <Stack gap="sm" hiddenFrom="xs">
            <SegmentedControl
              fullWidth
              value={String(selectedDayIndex)}
              onChange={(value) => setSelectedDayIndex(Number(value))}
              data={weekDates.map((date, i) => ({
                value: String(i),
                label: formatDateOnly(date, i18n.language, { weekday: "short", day: "2-digit" }),
              }))}
            />
            <Stack gap="xs">
              {visibleEmployees.map((employee) => (
                <ShiftPlanCard
                  key={employee.id}
                  employeeName={employee.displayName}
                  assignment={assignmentsByEmployee.get(employee.id)?.get(selectedDate)}
                />
              ))}
            </Stack>
          </Stack>

          <ShiftCalendarGrid
            employees={visibleEmployees}
            dates={weekDates}
            assignmentsByEmployee={assignmentsByEmployee}
            definitionNames={definitionNames}
            selectedKeys={selectedKeys}
            editable={canEditCells}
            totalsByDate={totalsByDate}
            language={i18n.language}
            isDateLocked={isDateLocked}
            onCellClick={handleCellClick}
            onCellExtend={handleCellExtend}
            onCellActivate={handleCellActivate}
            onCellDrop={handleCellDrop}
          />
        </>
      )}

      <ConfirmDialog
        open={clearing}
        onOpenChange={setClearing}
        title={t("attendance:shiftPlan.clearShiftsTitle")}
        description={t("attendance:shiftPlan.clearShiftsDescription", { count: selectedKeys.size })}
        isLoading={clearMutation.isPending}
        onConfirm={() => clearMutation.mutate()}
      />

      <ConfirmDialog
        open={publishing}
        onOpenChange={setPublishing}
        title={t("attendance:shiftPlan.publishTitle")}
        description={t("attendance:shiftPlan.publishDescription", { count: draftCount })}
        confirmLabel={t("attendance:shiftPlan.publish")}
        confirmVariant="default"
        isLoading={publishMutation.isPending}
        onConfirm={() => publishMutation.mutate()}
      />

      <ConfirmDialog
        open={locking}
        onOpenChange={setLocking}
        title={t("attendance:shiftPlan.lockTitle")}
        description={t("attendance:shiftPlan.lockDescription", { month: formatDateOnly(periodMonth, i18n.language, { month: "long", year: "numeric" }) })}
        confirmLabel={t("attendance:shiftPlan.lock")}
        confirmVariant="default"
        isLoading={lockMutation.isPending}
        onConfirm={() => lockMutation.mutate()}
      />

      <Modal
        opened={unlocking}
        onClose={() => {
          setUnlocking(false);
          setUnlockReason("");
          setUnlockError(null);
        }}
        title={t("attendance:shiftPlan.unlockTitle")}
        centered
      >
        <Stack gap="md">
          <Textarea
            label={t("attendance:shiftPlan.unlockReasonLabel")}
            value={unlockReason}
            autosize
            minRows={2}
            disabled={unlockMutation.isPending}
            onChange={(e) => setUnlockReason(e.currentTarget.value)}
          />
          {unlockError && (
            <Text size="sm" c="red" role="alert">
              {unlockError}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              disabled={unlockMutation.isPending}
              onClick={() => {
                setUnlocking(false);
                setUnlockReason("");
                setUnlockError(null);
              }}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              loading={unlockMutation.isPending}
              onClick={() => {
                if (!unlockReason.trim()) return setUnlockError(t("attendance:shiftPlan.unlockReasonRequired"));
                setUnlockError(null);
                unlockMutation.mutate();
              }}
            >
              {t("attendance:shiftPlan.unlock")}
            </Button>
          </Group>
        </Stack>
      </Modal>
      {canPlan && (
        <ShiftTemplateDialog
          opened={templatesOpen}
          onClose={() => setTemplatesOpen(false)}
          selectedKeys={selectedKeys}
          assignments={(assignmentsQuery.data ?? []) as CalendarShiftAssignment[]}
          weekStart={weekStartIso}
          onChanged={() => void invalidateAll()}
        />
      )}
    </div>
  );
}

/** Mobil gün seçici kart satırı (`team-timesheet.tsx`teki `TeamDayCard` kalıbı): tek çalışan × tek gün. */
function ShiftPlanCard({ employeeName, assignment }: { employeeName: string; assignment?: CalendarShiftAssignment }) {
  const { t } = useTranslation(["attendance"]);
  const nonWork = isNonWorkDay(assignment);

  return (
    <div
      className="w-full rounded-md border p-3"
      style={{ borderColor: "var(--mantine-color-default-border)", background: "var(--mantine-color-body)" }}
    >
      <Group justify="space-between" wrap="nowrap">
        <Text fw={500} truncate>
          {employeeName || "—"}
        </Text>
        <Text size="sm" c={assignment ? undefined : "dimmed"} style={{ whiteSpace: "nowrap" }}>
          {!assignment ? "—" : nonWork ? t(`attendance:shiftPlan.dayType.${assignment.dayType}`) : `${formatTime(assignment.start)} – ${formatTime(assignment.end)}`}
        </Text>
      </Group>
      {assignment && !nonWork && (assignment.isNight || assignment.status === "draft") && (
        <Group gap={4} wrap="wrap" mt={6}>
          {assignment.isNight && (
            <Badge size="xs" variant="light" color="indigo" leftSection={<Moon size={10} />}>
              {t("attendance:shiftPlan.night")}
            </Badge>
          )}
          {assignment.status === "draft" && (
            <Badge size="xs" variant="outline" color="gray">
              {t("attendance:shiftPlan.draft")}
            </Badge>
          )}
        </Group>
      )}
    </div>
  );
}
