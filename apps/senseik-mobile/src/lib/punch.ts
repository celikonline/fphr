import type { MyCheckInStatus } from "@/services/AttendanceService";
import { formatDurationMinutes } from "@/lib/formatters";

/** Giriş / Çıkış / Mola başı / Mola sonu - same set as the API's `PunchDirection`. */
export type PunchAction = "in" | "out" | "breakStart" | "breakEnd";

export const PUNCH_ACTIONS: readonly PunchAction[] = ["in", "out", "breakStart", "breakEnd"];

/** Tile title and the imperative button label for each punch. */
export const PUNCH_LABELS: Record<PunchAction, { tile: string; action: string }> = {
  in: { tile: "Giriş", action: "Giriş yap" },
  out: { tile: "Çıkış", action: "Çıkış yap" },
  breakStart: { tile: "Mola başı", action: "Molaya başla" },
  breakEnd: { tile: "Mola sonu", action: "Molayı bitir" },
};

/** QR button label: the scan records `next`, so it names that punch ("QR ile çıkış yap"). */
export function qrButtonLabel(next: PunchAction | "done"): string {
  if (next === "done") return "QR kodu okut";
  return `QR ile ${PUNCH_LABELS[next].action.toLocaleLowerCase("tr-TR")}`;
}

/** Spec §6: location accuracy at or below this many meters reads as "Doğru". */
const ACCURATE_METERS = 50;

/** The day's next punch: !checkIn → in · onBreak → breakEnd · checkIn && !checkOut → out · checkOut → done. */
export function nextPunchAction(s: MyCheckInStatus): PunchAction | "done" {
  if (!s.checkIn) return "in";
  if (s.checkOut) return "done";
  return s.onBreak ? "breakEnd" : "out";
}

/** Which tiles are tappable. Çıkış stays available during an open break - the server closes the break then. */
export function availableActions(s: MyCheckInStatus): Record<PunchAction, boolean> {
  const working = Boolean(s.checkIn) && !s.checkOut;
  const onBreak = working && Boolean(s.onBreak);
  return {
    in: !s.checkIn,
    out: working,
    breakStart: working && !onBreak,
    breakEnd: onBreak,
  };
}

function secondsOfDay(value: string): number | null {
  const match = /^(\d{2}):(\d{2})(?::(\d{2}))?/.exec(value);
  if (!match) return null;
  return Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3] ?? 0);
}

/**
 * "2 sa 12 dk" since the check-in time ("HH:mm:ss", tenant-local). A check-in later in the day than
 * `now` is a night shift that started yesterday evening, so it wraps past midnight.
 */
export function elapsedSinceCheckIn(checkIn: string | null | undefined, now: Date): string | null {
  const start = checkIn ? secondsOfDay(checkIn) : null;
  if (start === null) return null;
  const current = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();
  const elapsed = current >= start ? current - start : current + 24 * 3600 - start;
  return formatDurationMinutes(Math.floor(elapsed / 60));
}

/** Location accuracy badge from `expo-location`'s `coords.accuracy` (meters); `null` when unknown. */
export function accuracyLabel(meters: number | null | undefined): "Doğru" | "Zayıf" | null {
  if (meters === null || meters === undefined || Number.isNaN(meters)) return null;
  return meters <= ACCURATE_METERS ? "Doğru" : "Zayıf";
}

/**
 * iOS uses Apple Maps (no key). Android's Google Maps SDK crashes without an API key, so it only
 * renders when `app.config.js` saw `GOOGLE_MAPS_ANDROID_API_KEY` (`extra.hasMapsKey`).
 */
export function shouldRenderMap(
  extra: { hasMapsKey?: boolean } | undefined,
  platform: string
): boolean {
  if (platform === "ios") return true;
  if (platform === "android") return extra?.hasMapsKey === true;
  return false;
}

function hhmm(value: string | null | undefined): string | null {
  return value ? value.slice(0, 5) : null;
}

/**
 * Tile times ("HH:mm" or null). Giriş/Çıkış and an open break's start come from the status; a closed
 * break's start/end are not in the status, so they fall back to this session's punch results.
 */
export function punchTimes(
  s: MyCheckInStatus | null,
  session: Partial<Record<PunchAction, string>>
): Record<PunchAction, string | null> {
  return {
    in: hhmm(s?.checkIn),
    out: hhmm(s?.checkOut),
    breakStart: hhmm(s?.onBreak ? s.breakStartedAt : session.breakStart),
    breakEnd: s?.onBreak ? null : hhmm(session.breakEnd),
  };
}

/** "27 Eyl 2026". */
export function formatClockDate(date: Date): string {
  return date.toLocaleDateString("tr-TR", { day: "numeric", month: "short", year: "numeric" });
}

/** The device's UTC offset: "GMT+3", "GMT+5:30", "GMT-4", "GMT". */
export function formatGmtOffset(date: Date): string {
  const offset = -date.getTimezoneOffset();
  if (offset === 0) return "GMT";
  const sign = offset > 0 ? "+" : "-";
  const hours = Math.floor(Math.abs(offset) / 60);
  const minutes = Math.abs(offset) % 60;
  return `GMT${sign}${hours}${minutes ? `:${String(minutes).padStart(2, "0")}` : ""}`;
}
