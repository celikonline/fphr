import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { getAttendanceErrorMessage } from "@/lib/attendance-error";
import type { TFunction } from "i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Button,
  Drawer,
  Group,
  Input,
  NumberInput,
  Select,
  SegmentedControl,
  Skeleton,
  Stack,
  Table,
  Text,
  Textarea,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import { CalendarPlus, ChevronLeft, ChevronRight, Clock, Coffee, EyeOff, Moon, Pencil, Users } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { incalculableReasonText } from "@/components/attendance/incalculable-hint";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useLeaveName } from "@/components/leave/leave-shared";
import { TimePicker } from "@/components/ui/time-picker";
import { toast } from "@/hooks/use-toast";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { getEmployeeLeaveBalances } from "@/services/leave.service";
import {
  getEmployeeAttendanceDays,
  getTeamMonth,
  getTeamTimesheet,
  ignoreAnomaly,
  parseAnomalyFlags,
  resolveWithLeaveRequest,
  resolveWithOvertime,
  resolveWorkedHours,
  WEEK_DAYS_MONDAY_FIRST,
  type AnomalyResolutionAction,
  type TeamWeekDay,
  type TeamWeekRow,
} from "@/services/attendance.service";

const TEAM_KEY_PREFIX = ["attendance", "team-timesheet"] as const;

/** Monday of the calendar week that contains `date` (local time, no UTC shift). */
function mondayOf(date: Date): Date {
  const day = date.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  monday.setDate(monday.getDate() + diff);
  return monday;
}

/** Mola/gece/limit rozet açıklamaları (kart senseik-014 G-02): yalnız anlamlı olduğunda gösterilir. */
function dailyWorkTooltips(day: TeamWeekDay, t: TFunction): string[] {
  const tooltips: string[] = [];
  if (day.breakMinutesApplied > 0) {
    tooltips.push(t("attendance:teamTimesheet.breakApplied", { m: day.breakMinutesApplied }));
  }

  if (day.nightMinutes > 0) {
    tooltips.push(t("attendance:teamTimesheet.nightMinutes", { m: day.nightMinutes }));
  }

  if (day.dailyLimitExceeded) {
    tooltips.push(t("attendance:teamTimesheet.dailyLimitExceeded"));
  }

  if (day.nightLimitExceeded) {
    tooltips.push(t("attendance:teamTimesheet.nightLimitExceeded"));
  }

  if (day.incalculableReason) {
    tooltips.push(incalculableReasonText(t, day.incalculableReason));
  }

  return tooltips;
}

/** First day of the calendar month that contains `date` (local time). */
function monthStartOf(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** Every date ("yyyy-MM-dd") in the calendar month that starts at `monthStart`. */
function datesInMonth(monthStart: Date): string[] {
  const daysCount = new Date(monthStart.getFullYear(), monthStart.getMonth() + 1, 0).getDate();
  return Array.from({ length: daysCount }, (_, i) => toDateOnly(new Date(monthStart.getFullYear(), monthStart.getMonth(), i + 1)));
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
function formatTime(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "—";
}

/** "HH:mm" → "HH:mm:ss" for the API. */
function toApiTime(value: string): string {
  return `${value}:00`;
}

/**
 * Ekip haftalık puantaj ızgarası (11.3.6, S-8): satır = çalışan, kolon = gün (Pzt–Paz); hücrede çalışılan süre +
 * anomali rozeti. Hücre tıklaması gün detayı çekmecesini açar (kayıt varsa öneri butonlarıyla).
 */
export default function TeamTimesheetPage() {
  const canView = usePermission("attendance.timesheet.view");
  if (!canView) return <NoAccess />;
  return <TeamTimesheet />;
}

interface SelectedCell {
  employeeId: string;
  employeeName: string;
  date: string;
  attendanceDayId: string;
}

function TeamTimesheet() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const [granularity, setGranularity] = useState<"week" | "month">("week");
  const [weekStart, setWeekStart] = useState(() => mondayOf(new Date()));
  const [monthStart, setMonthStart] = useState(() => monthStartOf(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [selected, setSelected] = useState<SelectedCell | null>(null);
  // Mobile (dar viewport, ≤~576px): 7 kolonlu ızgara yerine gün seçici + çalışan kart listesi (bkz. TeamDayCard); Ay görünümünde kullanılmaz.
  const [selectedDayIndex, setSelectedDayIndex] = useState(() => {
    const day = new Date().getDay(); // 0 = Pazar
    return day === 0 ? 6 : day - 1; // Pazartesi-ilk index
  });

  const weekStartIso = toDateOnly(weekStart);
  const monthStartIso = toDateOnly(monthStart);
  const query = useQuery({
    queryKey: granularity === "week" ? [...TEAM_KEY_PREFIX, "week", weekStartIso, orgUnitId] : [...TEAM_KEY_PREFIX, "month", monthStartIso, orgUnitId],
    queryFn: () => (granularity === "week" ? getTeamTimesheet(weekStartIso, orgUnitId) : getTeamMonth(monthStartIso, orgUnitId)),
  });

  const weekDates = useMemo(() => WEEK_DAYS_MONDAY_FIRST.map((_, i) => toDateOnly(new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() + i))), [weekStart]);
  const monthDates = useMemo(() => datesInMonth(monthStart), [monthStart]);
  const rangeDates = granularity === "week" ? weekDates : monthDates;
  const weekEnd = weekDates[weekDates.length - 1];
  const selectedDate = weekDates[selectedDayIndex];

  const rows = [...(query.data ?? [])].sort((a, b) => a.employeeName.localeCompare(b.employeeName));

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Users size={24} /> {t("attendance:teamTimesheet.title")}
          </Title>
          <Text c="dimmed">{t("attendance:teamTimesheet.subtitle")}</Text>
        </div>
      </Group>

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <Group gap="sm" wrap="wrap">
          <OrgUnitSelect
            value={orgUnitId}
            onChange={setOrgUnitId}
            emptyLabel={t("attendance:teamTimesheet.allUnits")}
            className="w-64"
          />
          <SegmentedControl
            value={granularity}
            onChange={(value) => setGranularity(value as "week" | "month")}
            data={[
              { value: "week", label: t("attendance:teamTimesheet.week") },
              { value: "month", label: t("attendance:teamTimesheet.month") },
            ]}
          />
        </Group>
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={granularity === "week" ? t("attendance:teamTimesheet.previousWeek") : t("attendance:previousMonth")}>
            <ActionIcon
              variant="default"
              aria-label={granularity === "week" ? t("attendance:teamTimesheet.previousWeek") : t("attendance:previousMonth")}
              onClick={() =>
                granularity === "week"
                  ? setWeekStart((d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() - 7))
                  : setMonthStart((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))
              }
            >
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} miw={200} ta="center">
            {granularity === "week"
              ? `${formatDateOnly(weekStartIso, i18n.language, { day: "2-digit", month: "short" })} – ${formatDateOnly(weekEnd, i18n.language, { day: "2-digit", month: "short", year: "numeric" })}`
              : formatDateOnly(monthStartIso, i18n.language, { month: "long", year: "numeric" })}
          </Text>
          <Tooltip label={granularity === "week" ? t("attendance:teamTimesheet.nextWeek") : t("attendance:nextMonth")}>
            <ActionIcon
              variant="default"
              aria-label={granularity === "week" ? t("attendance:teamTimesheet.nextWeek") : t("attendance:nextMonth")}
              onClick={() =>
                granularity === "week"
                  ? setWeekStart((d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + 7))
                  : setMonthStart((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))
              }
            >
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {query.isLoading ? (
        <Stack gap="xs">
          <Skeleton h={40} />
          <Skeleton h={40} />
          <Skeleton h={40} />
        </Stack>
      ) : query.isError ? (
        <Text size="sm" c="red">
          {getAttendanceErrorMessage(query.error, t("attendance:genericError"), t)}
        </Text>
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("attendance:teamTimesheet.empty")}
        </Text>
      ) : (
        <>
          {/* Dar viewport (≤~576px), yalnız Hafta görünümünde: 7 kolonlu ızgara sığmaz; gün seçici + tek günlük çalışan kart listesi. Ay görünümünde (28-31 kolon) mobilde de kaydırmalı tablo kullanılır. */}
          {granularity === "week" && (
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
                {rows.map((row) => {
                  const day = row.days.find((d) => d.date === selectedDate);
                  if (!day) return null;
                  return (
                    <TeamDayCard
                      key={row.employeeId}
                      employeeName={row.employeeName}
                      day={day}
                      onClick={
                        day.attendanceDayId
                          ? () => setSelected({ employeeId: row.employeeId, employeeName: row.employeeName, date: day.date, attendanceDayId: day.attendanceDayId! })
                          : undefined
                      }
                    />
                  );
                })}
              </Stack>
            </Stack>
          )}

          <div className="overflow-x-auto">
            <Table
              withTableBorder
              withColumnBorders
              verticalSpacing="xs"
              miw={granularity === "week" ? 800 : rangeDates.length * 44 + 160}
              visibleFrom={granularity === "week" ? "xs" : undefined}
            >
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("attendance:teamTimesheet.employee")}</Table.Th>
                  {rangeDates.map((date) => (
                    <Table.Th key={date} ta="center">
                      {formatDateOnly(date, i18n.language, granularity === "week" ? { weekday: "short", day: "2-digit" } : { day: "2-digit" })}
                    </Table.Th>
                  ))}
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rows.map((row) => (
                  <TeamWeekTableRow key={row.employeeId} row={row} onSelectDay={setSelected} />
                ))}
              </Table.Tbody>
            </Table>
          </div>
        </>
      )}

      {selected && (
        <TeamDayDrawer
          selected={selected}
          onClose={() => setSelected(null)}
          onResolved={() => query.refetch()}
        />
      )}
    </div>
  );
}

function TeamWeekTableRow({ row, onSelectDay }: { row: TeamWeekRow; onSelectDay: (cell: SelectedCell) => void }) {
  const { t } = useTranslation(["attendance"]);

  return (
    <Table.Tr>
      <Table.Td fw={500}>{row.employeeName || "—"}</Table.Td>
      {row.days.map((day) => (
        <Table.Td key={day.date} p={0}>
          <TeamWeekCell day={day} onClick={day.attendanceDayId ? () => onSelectDay({ employeeId: row.employeeId, employeeName: row.employeeName, date: day.date, attendanceDayId: day.attendanceDayId! }) : undefined} label={t} />
        </Table.Td>
      ))}
    </Table.Tr>
  );
}

function TeamWeekCell({ day, onClick, label: t }: { day: TeamWeekDay; onClick?: () => void; label: TFunction }) {
  const flags = day.anomalies;
  const hours = Math.floor(day.workedMinutes / 60);
  const rest = day.workedMinutes % 60;
  const duration = day.workedMinutes <= 0 ? "—" : rest === 0 ? t("attendance:duration.hoursOnly", { h: hours }) : t("attendance:duration.hoursMinutes", { h: hours, m: rest });
  const badgeColor = day.status === "ignored" ? "gray" : flags.length > 0 ? "red" : "green";
  const dailyWorkTooltipText = dailyWorkTooltips(day, t).join(" · ");

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={!onClick}
      className="flex w-full flex-col items-center gap-1 p-2 disabled:cursor-default"
      style={{ cursor: onClick ? "pointer" : "default", background: "transparent", border: "none" }}
    >
      <Text size="sm" c={day.workedMinutes <= 0 ? "dimmed" : undefined}>
        {duration}
      </Text>
      <Group gap={4} wrap="nowrap">
        {flags.length > 0 && (
          <Tooltip label={flags.map((flag) => t(`attendance:anomaly.${flag}`)).join(", ")} multiline maw={220}>
            <Badge size="xs" color={badgeColor} variant="light">
              {flags.length}
            </Badge>
          </Tooltip>
        )}
        {dailyWorkTooltipText && (
          <Tooltip label={dailyWorkTooltipText} multiline maw={220}>
            <Badge
              size="xs"
              color={day.dailyLimitExceeded || day.nightLimitExceeded || day.incalculableReason ? "orange" : "gray"}
              variant="outline"
              aria-label={dailyWorkTooltipText}
              leftSection={day.nightMinutes > 0 ? <Moon className="h-3 w-3" /> : <Coffee className="h-3 w-3" />}
            >
              {" "}
            </Badge>
          </Tooltip>
        )}
      </Group>
    </button>
  );
}

/** Mobil gün seçici kart satırı (bkz. `TeamWeekCell`'in masaüstü karşılığı): tek çalışan × tek gün. */
function TeamDayCard({ employeeName, day, onClick }: { employeeName: string; day: TeamWeekDay; onClick?: () => void }) {
  const { t } = useTranslation(["attendance"]);
  const flags = day.anomalies;
  const hours = Math.floor(day.workedMinutes / 60);
  const rest = day.workedMinutes % 60;
  const duration = day.workedMinutes <= 0 ? "—" : rest === 0 ? t("attendance:duration.hoursOnly", { h: hours }) : t("attendance:duration.hoursMinutes", { h: hours, m: rest });
  const badgeColor = day.status === "ignored" ? "gray" : flags.length > 0 ? "red" : "green";
  const dailyWorkTooltipText = dailyWorkTooltips(day, t).join(" · ");

  return (
    <UnstyledButton
      onClick={onClick}
      disabled={!onClick}
      className="w-full rounded-md border p-3 text-left disabled:cursor-default"
      style={{
        borderColor: "var(--mantine-color-default-border)",
        background: "var(--mantine-color-body)",
        cursor: onClick ? "pointer" : "default",
      }}
    >
      <Group justify="space-between" wrap="nowrap">
        <Text fw={500} truncate>
          {employeeName || "—"}
        </Text>
        <Text size="sm" c={day.workedMinutes <= 0 ? "dimmed" : undefined} style={{ whiteSpace: "nowrap" }}>
          {duration}
        </Text>
      </Group>
      {(flags.length > 0 || dailyWorkTooltipText) && (
        <Group gap={4} wrap="wrap" mt={6}>
          {flags.map((flag) => (
            <Badge key={flag} size="xs" color={badgeColor} variant="light">
              {t(`attendance:anomaly.${flag}`)}
            </Badge>
          ))}
          {dailyWorkTooltipText && (
            <Tooltip label={dailyWorkTooltipText} multiline maw={220}>
              <Badge
                size="xs"
                color={day.dailyLimitExceeded || day.nightLimitExceeded ? "orange" : "gray"}
                variant="outline"
                leftSection={day.nightMinutes > 0 ? <Moon className="h-3 w-3" /> : <Coffee className="h-3 w-3" />}
              >
                {" "}
              </Badge>
            </Tooltip>
          )}
        </Group>
      )}
    </UnstyledButton>
  );
}

const ACTION_ICON: Record<AnomalyResolutionAction, typeof Pencil> = {
  setWorkedHours: Pencil,
  addOvertime: Clock,
  createLeaveRequest: CalendarPlus,
  ignore: EyeOff,
};

function TeamDayDrawer({ selected, onClose, onResolved }: { selected: SelectedCell; onClose: () => void; onResolved: () => void }) {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const canResolve = usePermission("attendance.timesheet.resolve");
  const queryClient = useQueryClient();
  const [activeAction, setActiveAction] = useState<AnomalyResolutionAction | null>(null);

  const detailQuery = useQuery({
    queryKey: ["attendance", "team-timesheet", "day", selected.employeeId, selected.date],
    queryFn: async () => {
      const days = await getEmployeeAttendanceDays(selected.employeeId, { from: selected.date, to: selected.date });
      return days.find((d) => d.id === selected.attendanceDayId) ?? days[0] ?? null;
    },
  });

  const day = detailQuery.data;

  const invalidateAndClose = async () => {
    await queryClient.invalidateQueries({ queryKey: ["attendance", "team-timesheet"] });
    onResolved();
    setActiveAction(null);
    onClose();
  };

  const ignoreMutation = useMutation({
    mutationFn: () => ignoreAnomaly(selected.attendanceDayId),
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance:teamTimesheet.resolved") });
      await invalidateAndClose();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getAttendanceErrorMessage(err, t("attendance:genericError"), t) }),
  });

  const allowedActions = day?.allowedActions ?? [];

  return (
    <Drawer
      opened
      onClose={onClose}
      position="right"
      size="28rem"
      title={
        <Text component="span" fw={600} size="lg">
          {selected.employeeName}
        </Text>
      }
    >
      <Stack gap="lg">
        <Text size="sm" c="dimmed" mt={-8}>
          {formatDateOnly(selected.date, i18n.language, { weekday: "long", day: "2-digit", month: "long", year: "numeric" })}
        </Text>

        {detailQuery.isLoading ? (
          <Skeleton h={160} />
        ) : detailQuery.isError || !day ? (
          <Text size="sm" c="red">
            {getAttendanceErrorMessage(detailQuery.error, t("attendance:genericError"), t)}
          </Text>
        ) : (
          <>
            <dl className="grid grid-cols-2 gap-4">
              <Field label={t("attendance:day.checkIn")}>{formatTime(day.checkIn)}</Field>
              <Field label={t("attendance:day.checkOut")}>{formatTime(day.checkOut)}</Field>
              <Field label={t("attendance:teamTimesheet.planned")}>
                {formatTime(day.plannedStart)} – {formatTime(day.plannedEnd)}
              </Field>
              <Field label={t("attendance:day.worked")}>
                {day.workedMinutes <= 0 ? "—" : `${Math.floor(day.workedMinutes / 60)}s ${day.workedMinutes % 60}dk`}
              </Field>
              <Field label={t("attendance:teamTimesheet.status")}>
                <Badge variant="light" color={day.resolutionState === "unresolved" ? "red" : day.resolutionState === "ignored" ? "gray" : "green"}>
                  {t(`attendance:resolutionState.${day.resolutionState}`)}
                </Badge>
              </Field>
            </dl>
            {parseAnomalyFlags(day.anomalies).length > 0 && (
              <Group gap={4} wrap="wrap">
                {parseAnomalyFlags(day.anomalies).map((flag) => (
                  <Badge key={flag} color={day.resolutionState === "unresolved" ? "red" : "gray"} variant="light">
                    {t(`attendance:anomaly.${flag}`)}
                  </Badge>
                ))}
              </Group>
            )}

            {canResolve && day.resolutionState === "unresolved" && allowedActions.length > 0 && (
              <Stack gap="sm">
                <Text size="sm" fw={600}>
                  {t("attendance:teamTimesheet.suggestions")}
                </Text>
                {activeAction === null ? (
                  <Group gap="xs" wrap="wrap">
                    {allowedActions.map((action) => {
                      const Icon = ACTION_ICON[action];
                      return (
                        <Button
                          key={action}
                          type="button"
                          size="xs"
                          variant="default"
                          leftSection={<Icon size={14} />}
                          loading={action === "ignore" && ignoreMutation.isPending}
                          onClick={() => (action === "ignore" ? ignoreMutation.mutate() : setActiveAction(action))}
                        >
                          {t(`attendance:teamTimesheet.action.${action}`)}
                        </Button>
                      );
                    })}
                  </Group>
                ) : activeAction === "setWorkedHours" ? (
                  <SetWorkedHoursForm attendanceDayId={selected.attendanceDayId} onCancel={() => setActiveAction(null)} onSuccess={invalidateAndClose} />
                ) : activeAction === "addOvertime" ? (
                  <AddOvertimeForm attendanceDayId={selected.attendanceDayId} onCancel={() => setActiveAction(null)} onSuccess={invalidateAndClose} />
                ) : activeAction === "createLeaveRequest" ? (
                  <CreateLeaveRequestForm employeeId={selected.employeeId} attendanceDayId={selected.attendanceDayId} onCancel={() => setActiveAction(null)} onSuccess={invalidateAndClose} />
                ) : null}
              </Stack>
            )}
          </>
        )}

        <Group justify="flex-end" gap="xs" py="md" className="sticky bottom-0" bg="var(--mantine-color-body)" style={{ borderTop: "1px solid var(--mantine-color-default-border)" }}>
          <Button type="button" variant="default" onClick={onClose}>
            {t("common:close")}
          </Button>
        </Group>
      </Stack>
    </Drawer>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm">{children}</Text>
    </div>
  );
}

function SetWorkedHoursForm({ attendanceDayId, onCancel, onSuccess }: { attendanceDayId: string; onCancel: () => void; onSuccess: () => Promise<void> }) {
  const { t } = useTranslation(["attendance", "common"]);
  const [minutes, setMinutes] = useState<string | number>("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => resolveWorkedHours(attendanceDayId, { workedMinutes: Number(minutes), reason: reason.trim() || null }),
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance:teamTimesheet.resolved") });
      await onSuccess();
    },
    onError: (err) => setError(getAttendanceErrorMessage(err, t("attendance:genericError"), t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (minutes === "" || Number(minutes) < 0) return setError(t("attendance:teamTimesheet.validation.minutesRequired"));
    setError(null);
    mutation.mutate();
  }

  const busy = mutation.isPending;

  return (
    <form onSubmit={submit}>
      <Stack gap="sm">
        <NumberInput
          label={t("attendance:teamTimesheet.workedMinutes")}
          min={0}
          max={1440}
          allowDecimal={false}
          allowNegative={false}
          value={minutes}
          disabled={busy}
          onChange={setMinutes}
        />
        <Textarea label={t("attendance:teamTimesheet.reason")} value={reason} disabled={busy} autosize minRows={2} onChange={(e) => setReason(e.currentTarget.value)} />
        {error && (
          <Text size="sm" c="red" role="alert">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="xs">
          <Button type="button" variant="default" size="xs" onClick={onCancel} disabled={busy}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" size="xs" loading={busy}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function AddOvertimeForm({ attendanceDayId, onCancel, onSuccess }: { attendanceDayId: string; onCancel: () => void; onSuccess: () => Promise<void> }) {
  const { t } = useTranslation(["attendance", "common"]);
  const [startTime, setStartTime] = useState("");
  const [durationMinutes, setDurationMinutes] = useState<string | number>("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => resolveWithOvertime(attendanceDayId, { startTime: toApiTime(startTime), durationMinutes: Number(durationMinutes), description: description.trim() || null }),
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance:teamTimesheet.resolved") });
      await onSuccess();
    },
    onError: (err) => setError(getAttendanceErrorMessage(err, t("attendance:genericError"), t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!/^\d{2}:\d{2}$/.test(startTime)) return setError(t("attendance:teamTimesheet.validation.startTimeRequired"));
    if (durationMinutes === "" || Number(durationMinutes) <= 0) return setError(t("attendance:teamTimesheet.validation.durationRequired"));
    setError(null);
    mutation.mutate();
  }

  const busy = mutation.isPending;

  return (
    <form onSubmit={submit}>
      <Stack gap="sm">
        <Input.Wrapper label={t("attendance:teamTimesheet.startTime")} id="team-timesheet-overtime-start">
          <TimePicker id="team-timesheet-overtime-start" value={startTime} disabled={busy} onChange={setStartTime} />
        </Input.Wrapper>
        <NumberInput
          label={t("attendance:teamTimesheet.durationMinutes")}
          min={1}
          max={720}
          allowDecimal={false}
          allowNegative={false}
          value={durationMinutes}
          disabled={busy}
          onChange={setDurationMinutes}
        />
        <Textarea label={t("attendance:teamTimesheet.description")} value={description} disabled={busy} autosize minRows={2} onChange={(e) => setDescription(e.currentTarget.value)} />
        {error && (
          <Text size="sm" c="red" role="alert">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="xs">
          <Button type="button" variant="default" size="xs" onClick={onCancel} disabled={busy}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" size="xs" loading={busy}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function CreateLeaveRequestForm({ employeeId, attendanceDayId, onCancel, onSuccess }: { employeeId: string; attendanceDayId: string; onCancel: () => void; onSuccess: () => Promise<void> }) {
  const { t } = useTranslation(["attendance", "common"]);
  const leaveName = useLeaveName();
  const [leaveTypeId, setLeaveTypeId] = useState<string | null>(null);
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);

  const balancesQuery = useQuery({
    queryKey: ["leave", "balances", employeeId],
    queryFn: () => getEmployeeLeaveBalances(employeeId),
  });

  const mutation = useMutation({
    mutationFn: () => resolveWithLeaveRequest(attendanceDayId, { leaveTypeId: leaveTypeId!, description: description.trim() || null }),
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance:teamTimesheet.resolved") });
      await onSuccess();
    },
    onError: (err) => setError(getAttendanceErrorMessage(err, t("attendance:genericError"), t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!leaveTypeId) return setError(t("attendance:teamTimesheet.validation.leaveTypeRequired"));
    setError(null);
    mutation.mutate();
  }

  const busy = mutation.isPending;

  return (
    <form onSubmit={submit}>
      <Stack gap="sm">
        <Select
          label={t("attendance:teamTimesheet.leaveType")}
          placeholder={balancesQuery.isLoading ? t("common:loading") : undefined}
          data={(balancesQuery.data?.balances ?? []).map((item) => ({ value: item.balance.leaveTypeId, label: leaveName(item.leaveTypeName) }))}
          value={leaveTypeId}
          disabled={busy || balancesQuery.isLoading}
          onChange={setLeaveTypeId}
        />
        <Textarea label={t("attendance:teamTimesheet.description")} value={description} disabled={busy} autosize minRows={2} onChange={(e) => setDescription(e.currentTarget.value)} />
        {error && (
          <Text size="sm" c="red" role="alert">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="xs">
          <Button type="button" variant="default" size="xs" onClick={onCancel} disabled={busy}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" size="xs" loading={busy}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

