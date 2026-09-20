import { useMemo, useState, type CSSProperties, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Box, Button, Group, Paper, SegmentedControl, Skeleton, Stack, Text, Title, Tooltip } from "@mantine/core";
import { CalendarDays, ChevronLeft, ChevronRight, AlertCircle } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { useDays, useLeaveName } from "@/components/leave/leave-shared";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import { getLeaveCalendar, type LeaveCalendarEntry } from "@/services/leave.service";
import classes from "./leave-calendar.module.css";

/** Outlook-style view switch: month keeps the existing grid; week widens columns; day drops to a per-employee list. */
type CalendarView = "day" | "week" | "month";

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** Monday of the calendar week that contains `date` (local time, no UTC shift; same convention as attendance/shift-plan.tsx). */
function startOfWeek(date: Date): Date {
  const day = date.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + diff);
}

function addDays(date: Date, delta: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + delta);
}

interface CalendarRow {
  employeeId: string;
  employeeName: string;
  /** Entries per lane, so overlapping requests of one employee do not cover each other. */
  lanes: { entry: LeaveCalendarEntry; start: number; end: number }[][];
}

/** İzinler → Takvim: month view of approved and pending leave (leave.my.calendar + Calendar.ShowMenu). */
export default function LeaveCalendarPage() {
  const canView = usePermission("leave.my.calendar");
  const menuEnabled = useAccessSetting("showCalendarMenu");
  if (!canView || !menuEnabled) return <NoAccess />;
  return <LeaveCalendar />;
}

function LeaveCalendar() {
  const { t, i18n } = useTranslation(["leave", "common", "employees"]);
  const leaveName = useLeaveName();
  const days = useDays();
  const { nameById } = useOrgUnits();
  const [view, setView] = useState<CalendarView>("month");
  const [anchor, setAnchor] = useState(() => new Date());
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);

  // The visible range is scoped to the active view, so week/day views fetch far less than a full month
  // (a calendar month is at most 31 days, well within the API's 93-day limit; week is 7, day is 1).
  const dates = useMemo(() => {
    if (view === "day") return [anchor];
    if (view === "week") {
      const start = startOfWeek(anchor);
      return Array.from({ length: 7 }, (_, i) => addDays(start, i));
    }
    const start = startOfMonth(anchor);
    const count = new Date(start.getFullYear(), start.getMonth() + 1, 0).getDate();
    return Array.from({ length: count }, (_, i) => new Date(start.getFullYear(), start.getMonth(), i + 1));
  }, [anchor, view]);
  const from = toDateOnly(dates[0]);
  const to = toDateOnly(dates[dates.length - 1]);
  const today = toDateOnly(new Date());

  const query = useQuery({
    queryKey: ["leave", "calendar", from, to, orgUnitId],
    queryFn: () => getLeaveCalendar({ from, to, orgUnitId }),
    retry: false,
  });

  const typeLabel = (entry: LeaveCalendarEntry) =>
    entry.isMasked
      ? t("leave:calendar.masked")
      : entry.leaveTypeName
        ? leaveName(entry.leaveTypeName)
        : entry.leaveTypeCode
          ? t(`leave:code.${entry.leaveTypeCode}`)
          : t("leave:calendar.masked");

  const rows = useMemo<CalendarRow[]>(() => {
    const byEmployee = new Map<string, CalendarRow>();
    const sorted = [...(query.data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate) || b.endDate.localeCompare(a.endDate));
    for (const entry of sorted) {
      const start = Math.max(0, dayIndex(entry.startDate, from));
      const end = Math.min(dates.length - 1, dayIndex(entry.endDate, from));
      if (end < start) continue;
      const row = byEmployee.get(entry.employeeId) ?? { employeeId: entry.employeeId, employeeName: entry.employeeName, lanes: [] };
      const lane = row.lanes.find((items) => items.every((item) => item.end < start || item.start > end));
      if (lane) lane.push({ entry, start, end });
      else row.lanes.push([{ entry, start, end }]);
      byEmployee.set(entry.employeeId, row);
    }
    return [...byEmployee.values()].sort((a, b) => a.employeeName.localeCompare(b.employeeName, i18n.language));
  }, [query.data, from, dates.length, i18n.language]);

  // Week columns are wider so bars can show more than just the truncated type label (task requirement 3).
  const dayColumnMin = view === "week" ? "6.5rem" : "1.75rem";
  const gridTemplate: CSSProperties = { gridTemplateColumns: `minmax(9rem, 13rem) repeat(${dates.length}, minmax(${dayColumnMin}, 1fr))` };
  const weekday = new Intl.DateTimeFormat(i18n.language, { weekday: "narrow" });
  const isWeekend = (date: Date) => date.getDay() === 0 || date.getDay() === 6;

  const shift = (delta: number) =>
    setAnchor((current) => {
      if (view === "day") return addDays(current, delta);
      if (view === "week") return addDays(current, delta * 7);
      return new Date(current.getFullYear(), current.getMonth() + delta, 1);
    });

  const periodLabel = useMemo(() => {
    if (view === "day") {
      return new Intl.DateTimeFormat(i18n.language, { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(dates[0]);
    }
    if (view === "week") {
      const start = new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "short" }).format(dates[0]);
      const end = new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "short", year: "numeric" }).format(dates[dates.length - 1]);
      return `${start} – ${end}`;
    }
    return new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(dates[0]);
  }, [view, dates, i18n.language]);

  const previousLabel = view === "day" ? t("leave:calendar.previousDay") : view === "week" ? t("leave:calendar.previousWeek") : t("leave:calendar.previous");
  const nextLabel = view === "day" ? t("leave:calendar.nextDay") : view === "week" ? t("leave:calendar.nextWeek") : t("leave:calendar.next");

  const tooltip = (entry: LeaveCalendarEntry) => (
    <Stack gap={2}>
      <Text size="xs" fw={600}>
        {entry.employeeName}
      </Text>
      <Text size="xs">{typeLabel(entry)}</Text>
      <Text size="xs">
        {formatDateOnly(entry.startDate, i18n.language)} – {formatDateOnly(entry.endDate, i18n.language)} · {days(entry.calculatedDays)}{" "}
        {t("leave:days")}
      </Text>
      <Text size="xs">{t(`leave:status.${entry.status}`)}</Text>
    </Stack>
  );

  // Calculate overlapping approved leave counts for each day
  const overlappingCountByDay = useMemo<Map<string, number>>(() => {
    const counts = new Map<string, number>();
    if (!query.data) return counts;
    for (let i = 0; i < dates.length; i++) {
      const dateStr = toDateOnly(dates[i]);
      let maxOverlap = 0;
      for (const entry of query.data) {
        if (entry.startDate <= dateStr && entry.endDate >= dateStr && entry.overlappingApprovedCount) {
          maxOverlap = Math.max(maxOverlap, entry.overlappingApprovedCount);
        }
      }
      if (maxOverlap > 0) {
        counts.set(dateStr, maxOverlap);
      }
    }
    return counts;
  }, [query.data, dates]);

  /**
   * One day's employees rendered row by row (requirement 4: day view detail list).
   * Reused as the always-visible day view content and as the narrow-screen fallback for week/month.
   */
  const dayGroup = (date: Date): ReactNode => {
    const key = toDateOnly(date);
    const overlapCount = overlappingCountByDay.get(key);
    const entries = (query.data ?? [])
      .filter((entry) => entry.startDate <= key && entry.endDate >= key)
      .sort((a, b) => a.employeeName.localeCompare(b.employeeName, i18n.language));
    if (entries.length === 0) return null;
    return (
      <div key={key} className={`${classes.dayGroup} flex flex-col gap-2 p-3`} data-weekend={isWeekend(date) || undefined}>
        <div className="flex items-center gap-2">
          <Text size="sm" fw={600} c={key === today ? "var(--mantine-primary-color-filled)" : undefined}>
            {formatDateOnly(key, i18n.language, { weekday: "long", day: "numeric", month: "long" })}
          </Text>
          {overlapCount && overlapCount > 0 && (
            <Tooltip label={t("leave:calendar.overlappingCount", { count: overlapCount })}>
              <AlertCircle className="h-4 w-4 text-amber-600 dark:text-amber-500 shrink-0" />
            </Tooltip>
          )}
        </div>
        <ul className="flex flex-col gap-1.5">
          {entries.map((entry) => (
            <li key={entry.requestId} className="flex items-center gap-2">
              <span className={`${classes.swatch} inline-block h-3 w-3 shrink-0`} data-status={entry.status} />
              <Text span size="sm" fw={500}>
                {entry.employeeName}
              </Text>
              <Text span size="sm" c="dimmed" className="truncate">
                {typeLabel(entry)} · {days(entry.calculatedDays)} {t("leave:days")}
              </Text>
            </li>
          ))}
        </ul>
      </div>
    );
  };

  return (
    <div className={`${classes.root} flex max-w-[96rem] flex-col gap-6 p-6 md:p-10`}>
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <CalendarDays className="h-6 w-6" /> {t("leave:calendar.title")}
        </Title>
        <Text c="dimmed">{t("leave:calendar.subtitle")}</Text>
      </div>

      <Group justify="space-between" gap="sm">
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={previousLabel}>
            <ActionIcon type="button" variant="default" size="lg" aria-label={previousLabel} onClick={() => shift(-1)}>
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Button type="button" variant="default" size="sm" onClick={() => setAnchor(new Date())}>
            {t("leave:calendar.today")}
          </Button>
          <Tooltip label={nextLabel}>
            <ActionIcon type="button" variant="default" size="lg" aria-label={nextLabel} onClick={() => shift(1)}>
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Title order={2} size="h4" ml="xs" tt="capitalize">
            {periodLabel}
          </Title>
        </Group>
        <Group gap="md">
          <SegmentedControl
            value={view}
            onChange={(value) => setView(value as CalendarView)}
            data={[
              { value: "day", label: t("leave:calendar.view.day") },
              { value: "week", label: t("leave:calendar.view.week") },
              { value: "month", label: t("leave:calendar.view.month") },
            ]}
          />
          <Group gap="sm" wrap="nowrap">
            <Group gap={6} wrap="nowrap">
              <span className={`${classes.swatch} inline-block h-3 w-5`} data-status="approved" />
              <Text size="xs" c="dimmed">
                {t("leave:status.approved")}
              </Text>
            </Group>
            <Group gap={6} wrap="nowrap">
              <span className={`${classes.swatch} inline-block h-3 w-5`} data-status="pending" />
              <Text size="xs" c="dimmed">
                {t("leave:status.pending")}
              </Text>
            </Group>
          </Group>
          {nameById.size > 0 && (
            <div className="w-64">
              <label htmlFor="calendar-unit" className="sr-only">
                {t("employees:fields.orgUnit")}
              </label>
              <OrgUnitSelect id="calendar-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
            </div>
          )}
        </Group>
      </Group>

      {/* Paper and the sticky name column both use --mantine-color-body, so they match in both schemes. */}
      <Paper withBorder className="overflow-hidden">
        <div>
          {query.isLoading ? (
            <div className="p-4">
              <Skeleton h={256} />
            </div>
          ) : query.isError ? (
            <Text size="sm" c="red" p="md">
              {getApiErrorMessage(query.error, t("leave:genericError"))}
            </Text>
          ) : rows.length === 0 ? (
            <Text size="sm" c="dimmed" p="md">
              {t("leave:calendar.empty")}
            </Text>
          ) : view === "day" ? (
            /* Day view: always the detailed per-employee list (requirement 4) — a single day has no lanes to lay out. */
            <div className={classes.dayList}>{dates.map((date) => dayGroup(date))}</div>
          ) : (
            <>
              {/* md+: employees × days grid (month: original cell size; week: wider columns, requirement 3) */}
              <div className="hidden overflow-x-auto md:block">
                <Box fz="xs" className="min-w-max">
                  <div className={`${classes.headerRow} grid`} style={gridTemplate}>
                    <div className={`${classes.nameHeader} sticky left-0 z-20 px-3 py-2`}>{t("leave:calendar.employee")}</div>
                    {dates.map((date) => {
                      const key = toDateOnly(date);
                      const overlapCount = overlappingCountByDay.get(key);
                      return (
                        <div
                          key={key}
                          className={`${classes.dayHeader} flex flex-col items-center py-1`}
                          data-weekend={isWeekend(date) || undefined}
                          data-today={key === today || undefined}
                        >
                          <span>{weekday.format(date)}</span>
                          <span>{date.getDate()}</span>
                          {overlapCount && overlapCount > 0 && (
                            <Tooltip label={t("leave:calendar.overlappingCount", { count: overlapCount })}>
                              <AlertCircle className="h-3 w-3 text-amber-600 dark:text-amber-500" />
                            </Tooltip>
                          )}
                        </div>
                      );
                    })}
                  </div>
                  {rows.map((row) => (
                    <div key={row.employeeId} className={`${classes.row} grid`} style={{ ...gridTemplate, gridAutoRows: "1.75rem" }}>
                      <div
                        className={`${classes.nameCell} sticky left-0 z-20 flex items-center truncate px-3`}
                        style={{ gridColumn: 1, gridRow: `1 / span ${row.lanes.length}` }}
                        title={row.employeeName}
                      >
                        {row.employeeName}
                      </div>
                      {dates.map((date, index) => (
                        <div
                          key={index}
                          aria-hidden
                          className={classes.dayCell}
                          data-weekend={isWeekend(date) || undefined}
                          data-today={toDateOnly(date) === today || undefined}
                          style={{ gridColumn: index + 2, gridRow: `1 / span ${row.lanes.length}` }}
                        />
                      ))}
                      {row.lanes.flatMap((lane, laneIndex) =>
                        lane.map(({ entry, start, end }) => (
                          <Tooltip key={entry.requestId} label={tooltip(entry)} multiline>
                            <div
                              tabIndex={0}
                              className={`${classes.bar} relative z-10 mx-0.5 my-1 flex items-center overflow-hidden px-1.5`}
                              data-status={entry.status}
                              style={{ gridColumn: `${start + 2} / ${end + 3}`, gridRow: laneIndex + 1 }}
                            >
                              <span className="truncate">
                                {typeLabel(entry)}
                                {view === "week" && ` · ${days(entry.calculatedDays)} ${t("leave:days")}`}
                              </span>
                            </div>
                          </Tooltip>
                        )),
                      )}
                    </div>
                  ))}
                </Box>
              </div>

              {/* Narrow screens: list by day */}
              <div className={`${classes.dayList} md:hidden`}>{dates.map((date) => dayGroup(date))}</div>
            </>
          )}
        </div>
      </Paper>
    </div>
  );
}

/** Whole days from `origin` to `value` (both DateOnly strings). */
function dayIndex(value: string, origin: string): number {
  return Math.round((parseDateOnly(value).getTime() - parseDateOnly(origin).getTime()) / 86_400_000);
}
