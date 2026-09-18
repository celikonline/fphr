import { Fragment, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Group, Paper, Text } from "@mantine/core";
import { Users } from "lucide-react";
import { addDaysToDateOnly, enumerateDateOnlyRange, formatDateOnly, parseDateOnly } from "@/lib/dates";
import type { LeaveTeamOverlap } from "@/services/leave.service";

/** Days of context shown before/after the request's own date range, so a peer's leave that only starts/ends near the edge is still legible. */
const CONTEXT_DAYS = 3;
/** Rows shown before the "Tümünü Gör (N)" expander appears. */
const DEFAULT_VISIBLE_ROWS = 5;
/** Rotates per row; Mantine theme colors so bars stay consistent with the rest of the app in light/dark mode. */
const BAR_COLORS = ["blue", "grape", "teal", "orange", "pink", "cyan", "indigo", "lime"];

interface TeamOverlapCalendarProps {
  overlap: LeaveTeamOverlap;
  /** The reviewed request's own dates (`DateOnly`) - define the grid's "requested" columns. */
  requestStart: string;
  requestEnd: string;
  locale: string;
}

interface DateRange {
  start: string;
  end: string;
}

/** Clips [start, end] to [windowStart, windowEnd]; null when the two ranges do not intersect. */
function clipToWindow(start: string, end: string, windowStart: string, windowEnd: string): DateRange | null {
  const clippedStart = start < windowStart ? windowStart : start;
  const clippedEnd = end > windowEnd ? windowEnd : end;
  return clippedStart > clippedEnd ? null : { start: clippedStart, end: clippedEnd };
}

/**
 * "Çakışan izinler" (S-7/S-8, senseik-026): a day-by-day grid instead of a plain text list - each row is a
 * colleague, each column a day (the request's own range plus a few context days), and a colored bar marks where
 * that colleague's own leave falls within the visible window. `overlap.employees` is already capped server-side
 * (`TeamOverlapMaxEmployees`); this component further collapses long lists behind "Tümünü Gör (N)".
 */
export function TeamOverlapCalendar({ overlap, requestStart, requestEnd, locale }: TeamOverlapCalendarProps) {
  const { t } = useTranslation(["leave"]);
  const [expanded, setExpanded] = useState(false);

  const windowStart = addDaysToDateOnly(requestStart, -CONTEXT_DAYS);
  const windowEnd = addDaysToDateOnly(requestEnd, CONTEXT_DAYS);
  const columns = useMemo(() => enumerateDateOnlyRange(windowStart, windowEnd), [windowStart, windowEnd]);
  const dayFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { day: "2-digit" }), [locale]);
  const weekdayFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { weekday: "short" }), [locale]);

  const visibleEmployees = expanded ? overlap.employees : overlap.employees.slice(0, DEFAULT_VISIBLE_ROWS);
  const canExpand = !expanded && overlap.employees.length > DEFAULT_VISIBLE_ROWS;
  // Server caps the fetched list (TeamOverlapMaxEmployees); this is the remainder never sent to the client at all.
  const notFetchedCount = overlap.count - overlap.employees.length;

  return (
    <Paper withBorder p="sm" radius="md">
      <Group gap="xs" mb={2}>
        <Users size={16} color="var(--mantine-color-dimmed)" />
        <Text size="sm" fw={600}>
          {t("leave:detail.overlappingLeaves.title", { count: overlap.count })}
        </Text>
      </Group>
      {overlap.orgUnitName && (
        <Text size="xs" c="dimmed" mb="xs">
          {overlap.orgUnitName}
        </Text>
      )}
      <div className="overflow-x-auto">
        <div
          className="grid items-center gap-y-1"
          style={{ gridTemplateColumns: `minmax(7rem, auto) repeat(${columns.length}, minmax(1.75rem, 1fr))` }}
        >
          <div />
          {columns.map((day) => {
            const isRequestedDay = day >= requestStart && day <= requestEnd;
            return (
              <div key={day} className="text-center">
                <Text size="xs" c={isRequestedDay ? undefined : "dimmed"} fw={isRequestedDay ? 600 : undefined}>
                  {weekdayFormatter.format(parseDateOnly(day))}
                </Text>
                <Text size="xs" c={isRequestedDay ? undefined : "dimmed"} fw={isRequestedDay ? 600 : undefined}>
                  {dayFormatter.format(parseDateOnly(day))}
                </Text>
              </div>
            );
          })}
          {visibleEmployees.map((employee, index) => {
            const bar = clipToWindow(employee.startDate, employee.endDate, windowStart, windowEnd);
            const color = BAR_COLORS[index % BAR_COLORS.length];
            const rangeLabel = `${formatDateOnly(employee.startDate, locale)} – ${formatDateOnly(employee.endDate, locale)}`;
            return (
              <Fragment key={employee.employeeId}>
                <div className="min-w-0">
                  <Text size="sm" truncate="end" title={employee.name}>
                    {employee.name}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {rangeLabel}
                  </Text>
                </div>
                {columns.map((day) => (
                  // Decorative: the accessible date range is already text next to the employee's name above.
                  <div key={day} className="flex h-4 items-center justify-center" aria-hidden="true">
                    {bar && day >= bar.start && day <= bar.end && (
                      <div
                        title={`${employee.name}: ${rangeLabel}`}
                        className="h-3 w-full rounded-sm"
                        style={{ backgroundColor: `var(--mantine-color-${color}-6)` }}
                        data-testid={`overlap-bar-${employee.employeeId}`}
                      />
                    )}
                  </div>
                ))}
              </Fragment>
            );
          })}
        </div>
      </div>
      {canExpand && (
        <Button variant="subtle" size="xs" mt="xs" px={0} onClick={() => setExpanded(true)}>
          {t("leave:detail.overlappingLeaves.viewAll", { count: overlap.employees.length })}
        </Button>
      )}
      {notFetchedCount > 0 && (
        <Text size="xs" c="dimmed" mt={4}>
          {t("leave:detail.overlappingLeaves.more", { count: notFetchedCount })}
        </Text>
      )}
    </Paper>
  );
}
