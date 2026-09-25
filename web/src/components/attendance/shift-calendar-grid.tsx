import { useState, type DragEvent, type KeyboardEvent, type MouseEvent } from "react";
import { useTranslation } from "react-i18next";
import { Badge, Group, Stack, Table, Text } from "@mantine/core";
import { Moon } from "lucide-react";
import { formatDateOnly } from "@/lib/dates";
import type { CalendarShiftAssignment, ShiftDayTotal } from "@/services/attendance-shift-calendar.service";
import { decodePalettePayload, SHIFT_PALETTE_MIME, type PalettePayload } from "./shift-palette";

export const cellKey = (employeeId: string, date: string) => `${employeeId}|${date}`;

/** Rectangle between two cells (inclusive) over the visible row / column order; null when either end is unknown. */
export function cellRange(
  employeeIds: readonly string[],
  dates: readonly string[],
  anchor: { employeeId: string; date: string },
  target: { employeeId: string; date: string },
): { employeeIds: string[]; dates: string[] } | null {
  const rows = [employeeIds.indexOf(anchor.employeeId), employeeIds.indexOf(target.employeeId)];
  const cols = [dates.indexOf(anchor.date), dates.indexOf(target.date)];
  if (rows.includes(-1) || cols.includes(-1)) return null;
  return {
    employeeIds: employeeIds.slice(Math.min(...rows), Math.max(...rows) + 1),
    dates: dates.slice(Math.min(...cols), Math.max(...cols) + 1),
  };
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
const formatTime = (value: string) => value.slice(0, 5);

export interface CellRef {
  employeeId: string;
  date: string;
}

const ARROW_STEPS: Record<string, [number, number]> = {
  ArrowUp: [-1, 0],
  ArrowDown: [1, 0],
  ArrowLeft: [0, -1],
  ArrowRight: [0, 1],
};

export interface CalendarEmployee {
  id: string;
  displayName: string;
}

interface ShiftCalendarGridProps {
  employees: CalendarEmployee[];
  dates: string[];
  assignmentsByEmployee: Map<string, Map<string, CalendarShiftAssignment>>;
  definitionNames: Map<string, string>;
  selectedKeys: Set<string>;
  /** False in a locked period or without `attendance.shift.plan`: no selection, no drop. */
  editable: boolean;
  totalsByDate: Map<string, ShiftDayTotal>;
  language: string;
  /** Per-cell lock (a week can span two months, each with its own lock). */
  isDateLocked: (date: string) => boolean;
  onCellClick: (employeeId: string, date: string, extend: boolean) => void;
  /** Keyboard Shift+arrow: extend the selection from `from` to `to`. */
  onCellExtend: (from: CellRef, to: CellRef) => void;
  /** Keyboard Enter / Space. */
  onCellActivate: (employeeId: string, date: string) => void;
  onCellDrop: (employeeId: string, date: string, payload: PalettePayload) => void;
}

export default function ShiftCalendarGrid({
  employees,
  dates,
  assignmentsByEmployee,
  definitionNames,
  selectedKeys,
  editable,
  totalsByDate,
  language,
  isDateLocked,
  onCellClick,
  onCellExtend,
  onCellActivate,
  onCellDrop,
}: ShiftCalendarGridProps) {
  const { t } = useTranslation(["attendance"]);
  // Roving tabindex: exactly one cell is in the tab order.
  const [focused, setFocused] = useState<CellRef | null>(null);
  const focusTarget =
    focused && employees.some((e) => e.id === focused.employeeId) && dates.includes(focused.date)
      ? focused
      : employees.length > 0 && dates.length > 0
        ? { employeeId: employees[0].id, date: dates[0] }
        : null;

  const handleKeyDown = (event: KeyboardEvent<HTMLElement>, current: CellRef) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      if (editable && !isDateLocked(current.date)) onCellActivate(current.employeeId, current.date);
      return;
    }
    const step = ARROW_STEPS[event.key];
    if (!step) return;
    event.preventDefault();
    const row = employees.findIndex((e) => e.id === current.employeeId) + step[0];
    const col = dates.indexOf(current.date) + step[1];
    if (row < 0 || row >= employees.length || col < 0 || col >= dates.length) return;
    const next = { employeeId: employees[row].id, date: dates[col] };
    setFocused(next);
    event.currentTarget
      .closest("table")
      ?.querySelector<HTMLElement>(`[data-cell-key="${cellKey(next.employeeId, next.date)}"]`)
      ?.focus();
    if (event.shiftKey && editable) onCellExtend(current, next);
  };

  const handleDragOver = (event: DragEvent<HTMLElement>, date: string) => {
    if (editable && !isDateLocked(date)) event.preventDefault();
  };
  const handleDrop = (event: DragEvent<HTMLElement>, employeeId: string, date: string) => {
    if (!editable || isDateLocked(date)) return;
    event.preventDefault();
    const payload = decodePalettePayload(event.dataTransfer.getData(SHIFT_PALETTE_MIME));
    if (payload) onCellDrop(employeeId, date, payload);
  };

  return (
    <div className="overflow-x-auto">
      <Table role="grid" aria-multiselectable withTableBorder withColumnBorders verticalSpacing="xs" miw={800} visibleFrom="xs">
        <Table.Thead>
          <Table.Tr role="row">
            <Table.Th>{t("attendance:shiftPlan.employee")}</Table.Th>
            {dates.map((date) => (
              <Table.Th key={date} ta="center">
                {formatDateOnly(date, language, { weekday: "short", day: "2-digit" })}
              </Table.Th>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {employees.map((employee) => {
            const byDate = assignmentsByEmployee.get(employee.id);
            return (
              <Table.Tr key={employee.id} role="row">
                <Table.Td fw={500} role="rowheader">
                  {employee.displayName}
                </Table.Td>
                {dates.map((date) => {
                  const selected = selectedKeys.has(cellKey(employee.id, date));
                  const assignment = byDate?.get(date);
                  const cellEditable = editable && !isDateLocked(date);
                  const isFocusTarget = focusTarget?.employeeId === employee.id && focusTarget.date === date;
                  return (
                    <Table.Td
                      key={date}
                      p={4}
                      role="gridcell"
                      tabIndex={isFocusTarget ? 0 : -1}
                      data-testid={`shift-cell-${employee.id}-${date}`}
                      data-cell-key={cellKey(employee.id, date)}
                      aria-selected={cellEditable ? selected : undefined}
                      aria-disabled={editable && !cellEditable ? true : undefined}
                      onFocus={() => setFocused({ employeeId: employee.id, date })}
                      onKeyDown={(event: KeyboardEvent<HTMLElement>) => handleKeyDown(event, { employeeId: employee.id, date })}
                      onClick={(event: MouseEvent) => cellEditable && onCellClick(employee.id, date, event.shiftKey)}
                      onDragOver={(event: DragEvent<HTMLElement>) => handleDragOver(event, date)}
                      onDrop={(event: DragEvent<HTMLElement>) => handleDrop(event, employee.id, date)}
                      style={{
                        cursor: cellEditable ? "pointer" : undefined,
                        background: selected ? "var(--mantine-primary-color-light)" : undefined,
                      }}
                    >
                      <ShiftCalendarCell
                        assignment={assignment}
                        definitionName={assignment?.shiftDefinitionId ? definitionNames.get(assignment.shiftDefinitionId) : undefined}
                      />
                    </Table.Td>
                  );
                })}
              </Table.Tr>
            );
          })}
        </Table.Tbody>
        <Table.Tfoot>
          <Table.Tr role="row">
            <Table.Th>{t("attendance:shiftPlan.totalsLabel")}</Table.Th>
            {dates.map((date) => (
              <Table.Td key={date} ta="center" data-testid={`shift-total-${date}`}>
                <DayTotal total={totalsByDate.get(date)} />
              </Table.Td>
            ))}
          </Table.Tr>
        </Table.Tfoot>
      </Table>
    </div>
  );
}

function DayTotal({ total }: { total?: ShiftDayTotal }) {
  const { t } = useTranslation(["attendance"]);
  if (!total || total.totalMinutes <= 0) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }
  return (
    <Stack gap={0} align="center">
      <Text size="sm" fw={600}>
        {formatMinutes(total.totalMinutes, t)}
      </Text>
      <Text size="xs" c="dimmed">
        {t("attendance:shiftPlan.totalPeople", { count: total.employeeCount })}
      </Text>
    </Stack>
  );
}

function formatMinutes(minutes: number, t: (key: string, options?: Record<string, unknown>) => string): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) return t("attendance:shiftPlan.totalMinutes", { minutes: rest });
  if (rest === 0) return t("attendance:shiftPlan.totalHours", { hours });
  return t("attendance:shiftPlan.totalHoursMinutes", { hours, minutes: rest });
}

/** Day type other than `work`: the hour fields are meaningless, only the type label is shown. */
export function isNonWorkDay(assignment?: CalendarShiftAssignment): boolean {
  return !!assignment?.dayType && assignment.dayType !== "work";
}

function ShiftCalendarCell({ assignment, definitionName }: { assignment?: CalendarShiftAssignment; definitionName?: string }) {
  const { t } = useTranslation(["attendance"]);
  if (!assignment) {
    return (
      <Text size="sm" c="dimmed" ta="center">
        —
      </Text>
    );
  }
  if (isNonWorkDay(assignment)) {
    return (
      <Stack gap={2} align="center">
        <Badge size="sm" variant="light" color="teal">
          {t(`attendance:shiftPlan.dayType.${assignment.dayType}`)}
        </Badge>
        {assignment.status === "draft" && <DraftBadge />}
      </Stack>
    );
  }
  return (
    <Stack gap={2} align="center">
      {definitionName && (
        <Text size="xs" fw={500} ta="center">
          {definitionName}
        </Text>
      )}
      <Text size="sm" ta="center">
        {formatTime(assignment.start)} – {formatTime(assignment.end)}
      </Text>
      {(assignment.isNight || assignment.status === "draft") && (
        <Group gap={4} justify="center" wrap="wrap">
          {assignment.isNight && (
            <Badge size="xs" variant="light" color="indigo" leftSection={<Moon size={10} />}>
              {t("attendance:shiftPlan.night")}
            </Badge>
          )}
          {assignment.status === "draft" && <DraftBadge />}
        </Group>
      )}
    </Stack>
  );
}

function DraftBadge() {
  const { t } = useTranslation(["attendance"]);
  return (
    <Badge size="xs" variant="outline" color="gray">
      {t("attendance:shiftPlan.draft")}
    </Badge>
  );
}
