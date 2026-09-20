import { useEffect, useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { DatePickerInput } from "@mantine/dates";
import { CalendarDays } from "lucide-react";
import { listHolidayCalendarEntries } from "@/services/structure.service";
import { toDateOnly } from "@/lib/dates";
import classes from "./date-picker.module.css";

export interface DatePickerProps {
  id?: string;
  /** Backend `DateOnly` string ("yyyy-MM-dd"), or "" when empty — same contract as `<Input type="date">`. */
  value: string;
  onChange: (value: string) => void;
  /** Earliest / latest selectable day, as "yyyy-MM-dd". */
  min?: string;
  max?: string;
  disabled?: boolean;
  /** Marks the input with the error styling (the message itself lives on the surrounding `Input.Wrapper`). */
  invalid?: boolean;
  placeholder?: string;
  /** Shows a clear button while a value is set (optional fields and filters). */
  clearable?: boolean;
  className?: string;
  /** When set, fetches that calendar's entries and marks its public holidays on the grid (distinct fill + dot). */
  holidayCalendarId?: string | null;
}

// senseik-041 Bug 2: opening a second <DatePicker> (e.g. the "end date" field right after "start date") while the
// first one's popover is still mounted/closing left both calendars visible at once, overlapping and translucent
// mid-transition. Mantine's DatePickerInput manages its own open state internally with no exclusivity across
// sibling instances, so we track "who is open" ourselves and hard-close the loser by remounting it (`key` bump) —
// this works regardless of *why* the first one was still open (slow close transition, tab focus, fast re-click).
const closeById = new Map<string, () => void>();
let openInstanceId: string | null = null;

function claimOpen(id: string): void {
  if (openInstanceId && openInstanceId !== id) closeById.get(openInstanceId)?.();
  openInstanceId = id;
}

/** "yyyy-MM-dd" or "MM-dd" (recurring holidays repeat every year) -> localized entry name. */
function buildHolidayIndex(entries: { date: string; name: string; isRecurringAnnually: boolean }[]): Map<string, string> {
  const index = new Map<string, string>();
  for (const entry of entries) {
    index.set(entry.isRecurringAnnually ? entry.date.slice(5) : entry.date, entry.name);
  }
  return index;
}

function toDateKey(date: string | Date): string {
  return typeof date === "string" ? date.slice(0, 10) : toDateOnly(date);
}

/** Mantine date picker input (click the calendar header for month/year levels); drop-in replacement for `<Input type="date">`. */
export function DatePicker({
  id,
  value,
  onChange,
  min,
  max,
  disabled,
  invalid,
  placeholder,
  clearable,
  className,
  holidayCalendarId,
}: DatePickerProps) {
  const { t } = useTranslation(["common"]);
  const instanceId = useId();
  const [generation, setGeneration] = useState(0);

  // Registers this instance so another <DatePicker> opening can force this one closed; unregister on unmount so a
  // stale entry never fires setState on an unmounted component.
  useEffect(() => {
    closeById.set(instanceId, () => setGeneration((prev) => prev + 1));
    return () => {
      closeById.delete(instanceId);
      if (openInstanceId === instanceId) openInstanceId = null;
    };
  }, [instanceId]);

  const holidaysQuery = useQuery({
    queryKey: ["structure", "holiday-calendar-entries", holidayCalendarId],
    queryFn: () => listHolidayCalendarEntries(holidayCalendarId as string),
    enabled: !!holidayCalendarId,
    staleTime: 60 * 60 * 1000,
    retry: false,
  });
  const holidayIndex = useMemo(() => buildHolidayIndex(holidaysQuery.data ?? []), [holidaysQuery.data]);

  function holidayName(date: string | Date): string | undefined {
    if (holidayIndex.size === 0) return undefined;
    const key = toDateKey(date);
    return holidayIndex.get(key) ?? holidayIndex.get(key.slice(5));
  }

  return (
    // display:contents keeps this a pure event-delegation wrapper — it adds no box, so it can't change layout
    // (grid/flex sizing) for any existing caller while still letting us see the click before Mantine's own
    // open-toggle handler runs (capture fires on ancestors before the target's own listeners).
    <div style={{ display: "contents" }} onPointerDownCapture={() => claimOpen(instanceId)}>
      <DatePickerInput
        key={generation}
        id={id}
        className={className}
        value={value || null}
        onChange={(next) => onChange(next ?? "")}
        minDate={min || undefined}
        maxDate={max || undefined}
        defaultDate={value || min || undefined}
        disabled={disabled}
        error={invalid || undefined}
        placeholder={placeholder ?? t("common:pickDate")}
        clearable={clearable}
        clearButtonProps={{ "aria-label": t("common:clear") }}
        valueFormat="DD.MM.YYYY"
        leftSection={<CalendarDays size={16} />}
        leftSectionPointerEvents="none"
        getDayProps={(date) => {
          const name = holidayName(date);
          return name ? { className: classes.holiday, title: name } : {};
        }}
        popoverProps={{ withinPortal: true }}
      />
    </div>
  );
}
