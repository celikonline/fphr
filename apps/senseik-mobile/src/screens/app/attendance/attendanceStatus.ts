import {
  parseAnomalyFlags,
  type AttendanceAnomalyFlag,
  type AttendanceDay,
} from "@/services/AttendanceService";
import type { Tone } from "@/theme/theme";
import i18n from "@/lib/i18n";

/** A Puantaj day card's status pill (spec §5). */
export type AttendanceDayStatus = "completed" | "missing" | "anomaly" | "onLeave" | "leave";

export type AttendanceFilter = "all" | "missing" | "anomaly";

const STATUS_TONES: Record<AttendanceDayStatus, Tone> = {
  completed: "success",
  missing: "danger",
  anomaly: "warning",
  // `workedWhileOnLeave`: the person worked on an approved-leave day, which needs a look.
  onLeave: "warning",
  // `day.onLeave` with nothing worked: an ordinary leave day, not an anomaly.
  leave: "info",
};

/** Status pill label (active language) and tone for a Puantaj day. */
export function attendanceStatusPill(status: AttendanceDayStatus): { label: string; tone: Tone } {
  return { label: i18n.t(`attendance:status.${status}`), tone: STATUS_TONES[status] };
}

const FILTER_VALUES: readonly AttendanceFilter[] = ["all", "missing", "anomaly"];

/** "Filtre" options in the active language. */
export function attendanceFilters(): { value: AttendanceFilter; label: string }[] {
  return FILTER_VALUES.map((value) => ({ value, label: i18n.t(`attendance:filters.${value}`) }));
}

/** Same wording as web (`attendance.json` → `anomaly.*`). */
const ANOMALY_FLAGS: readonly AttendanceAnomalyFlag[] = [
  "noData",
  "missingCheckIn",
  "missingCheckOut",
  "lateArrival",
  "earlyLeave",
  "lateLeave",
  "overwork",
  "underwork",
  "unplannedWork",
  "workedWhileOnLeave",
];

const MISSING_FLAGS = new Set<AttendanceAnomalyFlag>([
  "noData",
  "missingCheckIn",
  "missingCheckOut",
]);

/**
 * Status: work on a day covered by an approved leave (`workedWhileOnLeave`) wins first - it needs a
 * look even though the day is also `onLeave`. Then a plain leave day (`onLeave`, nothing worked) reads
 * as "İzinli" instead of "Eksik", even if the day also has no punches. Then a missing punch, then any
 * other anomaly flag.
 */
export function attendanceDayStatus(
  day: Pick<AttendanceDay, "anomalies"> & Partial<Pick<AttendanceDay, "onLeave" | "workedMinutes">>
): AttendanceDayStatus {
  const flags = parseAnomalyFlags(day.anomalies);
  if (flags.includes("workedWhileOnLeave")) return "onLeave";
  if (day.onLeave && (day.workedMinutes ?? 0) === 0) return "leave";
  if (flags.length === 0) return "completed";
  if (flags.some((flag) => MISSING_FLAGS.has(flag))) return "missing";
  return "anomaly";
}

/** Client-side "Filtre"; "Anomali" also keeps worked-on-leave days (that flag is an anomaly too). */
export function matchesAttendanceFilter(
  status: AttendanceDayStatus,
  filter: AttendanceFilter
): boolean {
  if (filter === "all") return true;
  if (filter === "missing") return status === "missing";
  return status === "anomaly" || status === "onLeave";
}

/** `TimeOnly` ("HH:mm:ss") → "HH:mm"; "—" when unset. */
export function formatTimeOfDay(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "—";
}

/** "Giriş 09:02 · Çıkış 18:10" */
export function formatPunchLine(day: Pick<AttendanceDay, "checkIn" | "checkOut">): string {
  return i18n.t("attendance:punchLine", {
    checkIn: formatTimeOfDay(day.checkIn),
    checkOut: formatTimeOfDay(day.checkOut),
  });
}

export function anomalyLabels(anomalies: string): string[] {
  return parseAnomalyFlags(anomalies).map((flag) =>
    ANOMALY_FLAGS.includes(flag) ? i18n.t(`attendance:anomaly.${flag}`) : flag
  );
}
