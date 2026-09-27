import {
  parseAnomalyFlags,
  type AttendanceAnomalyFlag,
  type AttendanceDay,
} from "@/services/AttendanceService";
import type { Tone } from "@/theme/theme";

/** A Puantaj day card's status pill (spec §5). */
export type AttendanceDayStatus = "completed" | "missing" | "anomaly" | "onLeave";

export type AttendanceFilter = "all" | "missing" | "anomaly";

export const ATTENDANCE_STATUS_PILLS: Record<AttendanceDayStatus, { label: string; tone: Tone }> = {
  completed: { label: "Tamamlandı", tone: "success" },
  missing: { label: "Eksik", tone: "danger" },
  anomaly: { label: "Anomali", tone: "warning" },
  onLeave: { label: "İzinli", tone: "info" },
};

export const ATTENDANCE_FILTERS: { value: AttendanceFilter; label: string }[] = [
  { value: "all", label: "Tümü" },
  { value: "missing", label: "Eksik" },
  { value: "anomaly", label: "Anomali" },
];

/** Same wording as web (`attendance.json` → `anomaly.*`). */
const ANOMALY_LABELS: Record<AttendanceAnomalyFlag, string> = {
  noData: "Kayıt yok",
  missingCheckIn: "Giriş eksik",
  missingCheckOut: "Çıkış eksik",
  lateArrival: "Geç giriş",
  earlyLeave: "Erken çıkış",
  lateLeave: "Geç çıkış",
  overwork: "Fazla çalışma",
  underwork: "Az çalışma",
  unplannedWork: "Plansız çalışma",
  workedWhileOnLeave: "İzinli günde çalışma",
};

const MISSING_FLAGS = new Set<AttendanceAnomalyFlag>([
  "noData",
  "missingCheckIn",
  "missingCheckOut",
]);

/**
 * Status from the day's anomaly flags (the DTO has no other leave/status field): a missing punch
 * wins, then a day covered by an approved leave (`workedWhileOnLeave`), then any other flag.
 */
export function attendanceDayStatus(day: Pick<AttendanceDay, "anomalies">): AttendanceDayStatus {
  const flags = parseAnomalyFlags(day.anomalies);
  if (flags.length === 0) return "completed";
  if (flags.some((flag) => MISSING_FLAGS.has(flag))) return "missing";
  if (flags.includes("workedWhileOnLeave")) return "onLeave";
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
  return `Giriş ${formatTimeOfDay(day.checkIn)} · Çıkış ${formatTimeOfDay(day.checkOut)}`;
}

export function anomalyLabels(anomalies: string): string[] {
  return parseAnomalyFlags(anomalies).map((flag) => ANOMALY_LABELS[flag] ?? flag);
}
