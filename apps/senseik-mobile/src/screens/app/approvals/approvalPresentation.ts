import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import { formatDays, formatDurationMinutes } from "@/lib/formatters";
import { formatTime, leaveSummaryTypeLabel, requestTypeLabel } from "./approvalLabels";
import type { InboxTask } from "@/services/ApprovalsService";

/** Kartın ikinci satırındaki büyük değer ("2 sa 30 dk" / "5 gün") - özet yoksa (Masraf/Avans/...
 * henüz sunucu tarafında sağlanmıyor, bkz. `ApprovalsService.ts`) `null`. */
export function approvalAmountText(task: Pick<InboxTask, "summary">): string | null {
  if (task.summary?.leave) {
    return `${formatDays(task.summary.leave.days)} gün`;
  }
  if (task.summary?.overtime) {
    return formatDurationMinutes(task.summary.overtime.durationMinutes);
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
export function approvalCardHeading(task: Pick<InboxTask, "requestType" | "summary">): ApprovalCardHeading {
  const amount = approvalAmountText(task);
  const typeCaption = approvalTypeCaption(task);
  if (amount) {
    return { heading: amount, subtitle: typeCaption };
  }
  return { heading: typeCaption, subtitle: null };
}

export type ApprovalDetailRow = { label: "Başlangıç" | "Bitiş" | "Tutar"; value: string };

/** Ayraçtan sonraki Başlangıç/Bitiş/Tutar satırları - yalnızca elde veri varsa (bkz. `approvalAmountText`). */
export function approvalDetailRows(task: Pick<InboxTask, "summary">): ApprovalDetailRow[] {
  if (task.summary?.leave) {
    return [
      { label: "Başlangıç", value: formatLeaveDateOnly(task.summary.leave.startDate) },
      { label: "Bitiş", value: formatLeaveDateOnly(task.summary.leave.endDate) },
    ];
  }
  if (task.summary?.overtime) {
    const { workDate, startTime, durationMinutes } = task.summary.overtime;
    return [
      { label: "Başlangıç", value: `${formatLeaveDateOnly(workDate)} · ${formatTime(startTime)}` },
      { label: "Tutar", value: formatDurationMinutes(durationMinutes) },
    ];
  }
  return [];
}
