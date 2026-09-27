import type {
  LeaveHalfDayPeriod,
  LeaveRequestPreview,
  LeaveUnit,
  RequestableLeaveType,
} from "@/services/LeaveService";
import { formatDays, formatDurationMinutes, parseTimeOnly } from "@/lib/formatters";

/**
 * Pure rules behind `LeaveRequestForm` (split out of it for the 300-line file budget; the form
 * re-exports them so existing imports keep working).
 */

/** D2: a full-day/half-day end date can't precede the (possibly just-changed) start date. */
export function clampEndDate(newStartDate: string, currentEndDate: string): string {
  return currentEndDate < newStartDate ? newStartDate : currentEndDate;
}

/**
 * A: live preview for an hourly request ("2 sa 30 dk") from the two "HH:mm" time fields; `null`
 * when either time is missing/malformed or the range is non-positive (mirrors
 * `LeaveRequestRules.ValidatePartialDayShape`'s `endTime <= startTime` rejection - no point
 * previewing a duration the server would reject anyway).
 */
export function formatHourlyDuration(startTime: string, endTime: string): string | null {
  const start = parseTimeOnly(startTime);
  const end = parseTimeOnly(endTime);
  if (!start || !end) return null;
  const totalMinutes = end.hours * 60 + end.minutes - (start.hours * 60 + start.minutes);
  if (totalMinutes <= 0) return null;
  return formatDurationMinutes(totalMinutes);
}

/** Derives the three mutually-exclusive per-unit form shapes from a `LeaveUnit`. */
export function leaveUnitFlags(unit: LeaveUnit | undefined): {
  isFullDay: boolean;
  isHalfDay: boolean;
  isHourly: boolean;
} {
  const isHalfDay = unit === "halfDay";
  const isHourly = unit === "hourly";
  return { isFullDay: !isHalfDay && !isHourly, isHalfDay, isHourly };
}

/**
 * B-mobil-006: builds the `GET .../requests/preview` query from the current form fields, or `null`
 * when there isn't enough information yet to ask for one (mirrors the same per-unit shape rules
 * `submitRequest` validates before posting, so the preview never fires a request the server would
 * reject for being incomplete).
 */
export function buildPreviewInput(
  selectedType: Pick<RequestableLeaveType, "id" | "unit"> | null,
  startDate: string,
  endDate: string,
  startTime: string,
  endTime: string,
  halfDayPeriod: LeaveHalfDayPeriod
) {
  if (!selectedType || !startDate) return null;
  const { isFullDay, isHalfDay, isHourly } = leaveUnitFlags(selectedType.unit);
  if (isFullDay && !endDate) return null;
  if (isHourly && !formatHourlyDuration(startTime, endTime)) return null;
  return {
    leaveTypeId: selectedType.id,
    start: startDate,
    end: isFullDay ? endDate : startDate,
    ...(isHourly ? { startTime: `${startTime}:00`, endTime: `${endTime}:00` } : {}),
    ...(isHalfDay ? { halfDayPeriod } : {}),
  };
}

/** B-mobil-006: "N gün izninizden düşülecek" / "Bu tarihlerde düşülecek gün yok". */
export function previewSummaryText(preview: LeaveRequestPreview | null): string | null {
  if (!preview) return null;
  return preview.days > 0
    ? `${formatDays(preview.days)} gün izninizden düşülecek`
    : "Bu tarihlerde düşülecek gün yok";
}

/** `LeaveDomainErrors.RequestZeroDays` (`LeaveDomainErrors.cs:25`) - same fact as `previewSummaryText`'s 0-day message. */
const ZERO_DAYS_WARNING_CODE = "leave.errors.zero_days";
/** `LeaveRequestCore.DocumentRequired` (`LeaveRequestCore.cs:42`) - same fact as `LeaveDocumentsSection`'s hint. */
const DOCUMENT_REQUIRED_WARNING_CODE = "leave.errors.document_required";

/**
 * B17: the preview always adds a `document_required` warning for a `requiresDocument` type (it has
 * no idea what's staged in this unsaved draft) and, once nothing would be deducted, a
 * `zero_days` warning duplicating `previewSummaryText`'s own message - both already have a client
 * counterpart on screen (the "Belge ekle" hint, the red 0-day summary), so showing the server's copy
 * too is pure repetition. Matched by **code**, never by message text - a translated/reworded message
 * must not silently stop being recognized as the same fact. Every other warning (balance/team
 * overlap/anything else) still gets through unchanged.
 */
export function filterRedundantWarnings(
  warnings: LeaveRequestPreview["warnings"],
  context: { previewDaysZero: boolean; requiresDocument: boolean }
): LeaveRequestPreview["warnings"] {
  return warnings.filter((warning) => {
    if (warning.code === ZERO_DAYS_WARNING_CODE && context.previewDaysZero) return false;
    if (warning.code === DOCUMENT_REQUIRED_WARNING_CODE && context.requiresDocument) return false;
    return true;
  });
}

/** B-mobil-004: a `requiresDocument` type needs at least one uploaded document before it can be submitted. */
export function hasRequiredDocument(
  type: Pick<RequestableLeaveType, "requiresDocument"> | null,
  documentCount: number
): boolean {
  return !type?.requiresDocument || documentCount > 0;
}

function sanitizeFileNameSegment(value: string): string {
  return value
    .replace(/[\\/:*?"<>|]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function extensionFromMimeType(mimeType: string): string {
  return mimeType === "image/png" ? "png" : "jpg";
}

/**
 * B10: the camera returns a UUID filename (e.g. "a2d952ee-...jpeg") that HR/the approver would
 * otherwise see verbatim as the attachment's name (`leave_request_attachments.file_name`). Build a
 * human-readable one instead - leave type name + local date/time, e.g.
 * "Hastalık izni 2026-09-28 13.24.jpg" (":" is not a safe filename character on every platform, so
 * the time separator is "." instead). Falls back to "Belge" when the leave type name is unknown.
 * Picker-selected files keep their own original name (`asset.name`) - this is only for camera shots.
 */
export function buildCapturedPhotoFileName(
  leaveTypeName: string | null | undefined,
  capturedAt: Date,
  mimeType: string
): string {
  const label = sanitizeFileNameSegment(leaveTypeName?.trim() || "Belge") || "Belge";
  const year = capturedAt.getFullYear();
  const month = String(capturedAt.getMonth() + 1).padStart(2, "0");
  const day = String(capturedAt.getDate()).padStart(2, "0");
  const hours = String(capturedAt.getHours()).padStart(2, "0");
  const minutes = String(capturedAt.getMinutes()).padStart(2, "0");
  return `${label} ${year}-${month}-${day} ${hours}.${minutes}.${extensionFromMimeType(mimeType)}`;
}

/**
 * Submit-time checks of the leave form, in the order the user is told about them; `null` when the
 * request can be sent. `hourlyDuration` is `formatHourlyDuration`'s result (only read for hourly
 * types); `previewBlocksSubmit` is true once the preview says no day would be deducted.
 */
export function leaveRequestBlocker(input: {
  selectedType: Pick<
    RequestableLeaveType,
    "unit" | "requiresDescription" | "requiresDocument" | "requiresSubstitute"
  > | null;
  startDate: string;
  endDate: string;
  hourlyDuration: string | null;
  description: string;
  hasSubstitute: boolean;
  documentCount: number;
  previewBlocksSubmit: boolean;
}): { title: string; message: string } | null {
  const { selectedType, startDate, endDate } = input;
  if (!selectedType) return { title: "Eksik bilgi", message: "Lütfen bir izin türü seçin." };
  if (!startDate) return { title: "Eksik bilgi", message: "Lütfen bir tarih girin." };
  const { isFullDay, isHourly } = leaveUnitFlags(selectedType.unit);
  if (isFullDay && !endDate) {
    return { title: "Eksik bilgi", message: "Lütfen başlangıç ve bitiş tarihini girin." };
  }
  if (isFullDay && endDate < startDate) {
    return { title: "Geçersiz tarih", message: "Bitiş tarihi başlangıç tarihinden önce olamaz." };
  }
  if (isHourly && !input.hourlyDuration) {
    return { title: "Geçersiz saat", message: "Bitiş saati başlangıç saatinden sonra olmalıdır." };
  }
  if (selectedType.requiresDescription && input.description.trim().length === 0) {
    return { title: "Eksik bilgi", message: "Bu izin türü için açıklama zorunludur." };
  }
  if (selectedType.requiresSubstitute && !input.hasSubstitute) {
    return { title: "Eksik bilgi", message: "Bu izin türü için bir vekil seçmeniz gerekir." };
  }
  if (!hasRequiredDocument(selectedType, input.documentCount)) {
    return {
      title: "Eksik bilgi",
      message: "Bu izin türü için en az bir belge eklemeniz gerekir.",
    };
  }
  if (input.previewBlocksSubmit) {
    return { title: "Gün düşülmeyecek", message: "Bu tarihlerde izninizden düşülecek gün yok." };
  }
  return null;
}
