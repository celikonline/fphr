import { TR_WEEKDAYS_SHORT, formatDayMonth, parseDateOnly } from "@/components/form/dateRange";
import { toLocalDateOnly } from "@/lib/formatters";

const DAYS_IN_WEEK = 7;

/** The seven local `DateOnly` days (Monday..Sunday) of the week containing `reference`. */
export function weekDays(reference: Date): string[] {
  const mondayOffset = (reference.getDay() + 6) % DAYS_IN_WEEK;
  return Array.from({ length: DAYS_IN_WEEK }, (_, index) =>
    toLocalDateOnly(
      new Date(
        reference.getFullYear(),
        reference.getMonth(),
        reference.getDate() - mondayOffset + index
      )
    )
  );
}

/** "28 Eyl – 4 Eki" for a `weekDays` result. */
export function formatWeekLabel(days: string[]): string {
  return `${formatDayMonth(days[0])} – ${formatDayMonth(days[days.length - 1])}`;
}

export function weekdayShort(value: string): string {
  const { year, month0, day } = parseDateOnly(value);
  return TR_WEEKDAYS_SHORT[(new Date(year, month0, day).getDay() + 6) % DAYS_IN_WEEK];
}

/** "Çar, 30 Eyl" */
export function formatDayLabel(value: string): string {
  return `${weekdayShort(value)}, ${formatDayMonth(value)}`;
}

/** Empty-list texts for Vardiyam's own/team sections: they speak of the day when one is picked in
 * the week strip, otherwise of the whole week. */
export function emptyShiftTexts(selectedDay: string | null): { mine: string; team: string } {
  const period = selectedDay ? "Bu gün" : "Bu hafta";
  return {
    mine: selectedDay
      ? `${period} için vardiyanız yok.`
      : `${period} için yayınlanmış vardiyanız yok.`,
    team: `${period} için görüntüleyebileceğiniz ekip vardiyası yok.`,
  };
}
