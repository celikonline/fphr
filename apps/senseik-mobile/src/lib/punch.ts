import type { CheckInBreak, MyCheckInStatus } from "@/services/AttendanceService";
import { getApiErrorMessage, getApiProblem } from "@/lib/apiError";
import { formatDate, formatDurationMinutes } from "@/lib/formatters";
import i18n from "@/lib/i18n";

/** Giriş / Çıkış / Mola başı / Mola sonu - same set as the API's `PunchDirection`. */
export type PunchAction = "in" | "out" | "breakStart" | "breakEnd";

export const PUNCH_ACTIONS: readonly PunchAction[] = ["in", "out", "breakStart", "breakEnd"];

/** Tile title ("Giriş") and the imperative button label ("Giriş yap") for a punch, in the active language. */
export function punchLabel(action: PunchAction): { tile: string; action: string } {
  return {
    tile: i18n.t(`checkin:punch.${action}.tile`),
    action: i18n.t(`checkin:punch.${action}.action`),
  };
}

/** QR button label: the scan records `next`, so it names that punch ("QR ile çıkış yap"). */
export function qrButtonLabel(next: PunchAction | "done"): string {
  return i18n.t(`checkin:qr.${next}`);
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

/** Location accuracy level from `expo-location`'s `coords.accuracy` (meters); `null` when unknown. */
export function accuracyLevel(meters: number | null | undefined): "good" | "weak" | null {
  if (meters === null || meters === undefined || Number.isNaN(meters)) return null;
  return meters <= ACCURATE_METERS ? "good" : "weak";
}

/** Location accuracy badge text ("Doğru" / "Zayıf") in the active language; `null` when unknown. */
export function accuracyLabel(meters: number | null | undefined): string | null {
  const level = accuracyLevel(meters);
  return level ? i18n.t(`checkin:accuracy.${level}`) : null;
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

/** The most recent break (`status.breaks` is oldest-first) - `null` when the day has none yet. */
export function latestBreak(breaks: CheckInBreak[] | null | undefined): CheckInBreak | null {
  if (!breaks || breaks.length === 0) return null;
  return breaks[breaks.length - 1];
}

/**
 * Tile times ("HH:mm" or null). Giriş/Çıkış always come from the status; Mola başı/sonu prefer the
 * latest entry of `status.breaks` (survives an app restart), falling back to this session's punch
 * results only when the status has no `breaks` at all.
 */
export function punchTimes(
  s: MyCheckInStatus | null,
  session: Partial<Record<PunchAction, string>>
): Record<PunchAction, string | null> {
  const latest = latestBreak(s?.breaks);
  return {
    in: hhmm(s?.checkIn),
    out: hhmm(s?.checkOut),
    breakStart: latest ? hhmm(latest.start) : hhmm(session.breakStart),
    breakEnd: latest ? hhmm(latest.end) : hhmm(session.breakEnd),
  };
}

/** Server code for a GPS/QR punch outside the location's radius (`SelfCheckInOutsideGeofence`). */
export const OUTSIDE_GEOFENCE_CODE = "attendance.self_checkin.outside_geofence";

/**
 * Punch error text. Outside the geofence gets the app's own message (the server's text says
 * "check-in" even for Çıkış/Mola); anything else is the server's localized detail or the fallback.
 */
export function punchErrorMessage(error: unknown): string {
  if (getApiProblem(error)?.code === OUTSIDE_GEOFENCE_CODE) {
    return i18n.t("checkin:geofence.outsideError");
  }
  return getApiErrorMessage(error, i18n.t("checkin:errors.punch"));
}

/**
 * FG-005 (Karar 3): runs the biometric `confirm` first and only then `send`s the punch; a declined or
 * cancelled confirmation sends nothing and resolves `null`.
 */
export async function sendConfirmedPunch<T>(
  confirm: () => Promise<boolean>,
  send: () => Promise<T>
): Promise<T | null> {
  if (!(await confirm())) return null;
  return send();
}

/** "27 Eyl 2026". */
export function formatClockDate(date: Date): string {
  return formatDate(date, { day: "numeric", month: "short", year: "numeric" });
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
