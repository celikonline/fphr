import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import { formatDays, formatDurationMinutes } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import { formatTime, leaveSummaryTypeLabel, requestTypeLabel } from "./approvalLabels";
import type { InboxTask } from "@/services/ApprovalsService";
import type { RequestDetailSummary } from "@/types/navigation";

/** Requested time - "—" when that side wasn't asked for (only one punch is being corrected). */
function formatOptionalTime(value?: string | null): string {
  return value ? formatTime(value) : "—";
}

/** Kartın ikinci satırındaki büyük değer ("2 sa 30 dk" / "5 gün") - özet yoksa (Masraf/Avans/...
 * henüz sunucu tarafında sağlanmıyor, bkz. `ApprovalsService.ts`) `null`. */
export function approvalAmountText(task: Pick<InboxTask, "summary">): string | null {
  if (task.summary?.leave) {
    const { days } = task.summary.leave;
    return i18n.t("common:units.days", { count: days, value: formatDays(days) });
  }
  if (task.summary?.overtime) {
    return formatDurationMinutes(task.summary.overtime.durationMinutes);
  }
  if (task.summary?.punchCorrection) {
    const { requestedCheckIn, requestedCheckOut } = task.summary.punchCorrection;
    return `${formatOptionalTime(requestedCheckIn)} – ${formatOptionalTime(requestedCheckOut)}`;
  }
  return null;
}

/** Aynı satırda değerin yanındaki tür etiketi (brandText) - İzin türü adı/"Fazla mesai"/genel `requestTypeLabel`. */
export function approvalTypeCaption(task: Pick<InboxTask, "requestType" | "summary">): string {
  if (task.summary?.leave) {
    return leaveSummaryTypeLabel(task.summary.leave);
  }
  return requestTypeLabel(task.requestType);
}

export type ApprovalCardHeading = {
  /** Kartın ikinci satırındaki büyük değer - tutar/süre varsa o, yoksa tür etiketi. */
  heading: string;
  /** `heading` zaten tür etiketiyse (Masraf/Avans/... için özet yok - QA-103) `null`: aynı metin
   *  iki kez basılmaz. */
  subtitle: string | null;
};

/** `amount`/`typeCaption`'ı kartın gösterebileceği tek bir çifte indirger - `amountText` yoksa
 *  (Masraf/Avans/... için sunucu henüz özet vermiyor) `heading` tür etiketine düşer ve `subtitle`
 *  boş kalır, aksi halde ikisi de ayrı ayrı gösterilir. */
export function approvalCardHeading(
  task: Pick<InboxTask, "requestType" | "summary">
): ApprovalCardHeading {
  const amount = approvalAmountText(task);
  const typeCaption = approvalTypeCaption(task);
  if (amount) {
    return { heading: amount, subtitle: typeCaption };
  }
  return { heading: typeCaption, subtitle: null };
}

export type ApprovalDetailRow = { label: string; value: string };

/** Ayraçtan sonraki Başlangıç/Bitiş/Tutar satırları - yalnızca elde veri varsa (bkz. `approvalAmountText`). */
export function approvalDetailRows(task: Pick<InboxTask, "summary">): ApprovalDetailRow[] {
  if (task.summary?.leave) {
    return [
      {
        label: i18n.t("approvals:rows.start"),
        value: formatLeaveDateOnly(task.summary.leave.startDate),
      },
      {
        label: i18n.t("approvals:rows.end"),
        value: formatLeaveDateOnly(task.summary.leave.endDate),
      },
    ];
  }
  if (task.summary?.overtime) {
    const { workDate, startTime, durationMinutes } = task.summary.overtime;
    return [
      {
        label: i18n.t("approvals:rows.start"),
        value: `${formatLeaveDateOnly(workDate)} · ${formatTime(startTime)}`,
      },
      { label: i18n.t("approvals:rows.amount"), value: formatDurationMinutes(durationMinutes) },
    ];
  }
  if (task.summary?.punchCorrection) {
    const { date, currentCheckIn, currentCheckOut } = task.summary.punchCorrection;
    return [
      { label: i18n.t("approvals:rows.date"), value: formatLeaveDateOnly(date) },
      {
        label: i18n.t("approvals:rows.current"),
        value: `${formatOptionalTime(currentCheckIn)} – ${formatOptionalTime(currentCheckOut)}`,
      },
    ];
  }
  return [];
}

/** FG-009: passed as `RequestDetail`'s `summary` param when the "Onaylarım" card is tapped - reuses
 * the same heading/rows this card already renders, so the detail screen shows type/amount/reason
 * instantly instead of waiting on a second call. */
export function approvalRequestDetailSummary(
  task: Pick<InboxTask, "requestType" | "summary">
): RequestDetailSummary {
  const { heading, subtitle } = approvalCardHeading(task);
  return {
    title: subtitle ?? heading,
    dateLabel: "",
    detail: heading,
    description:
      task.summary?.leave?.description ??
      task.summary?.overtime?.reason ??
      task.summary?.punchCorrection?.reason ??
      null,
  };
}
