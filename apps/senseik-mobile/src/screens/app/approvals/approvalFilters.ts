import i18n from "@/lib/i18n";
import { approvalTypeKey } from "./approvalLabels";

/** Both `InboxTask` (Onaylarım) and `ApprovalHistoryItem` (Geçmiş, FG-008) have a `requestType` -
 *  the filter only ever reads that field, so it works on either list. */
type FilterableTask = { requestType: string };

/** Görev 8: "Onaylarım"'ın "Tür" seçimi - `InboxTask.requestType` sunucu değerlerinden türetilir. */
export type ApprovalFilter = "all" | "leave" | "overtime" | "expense" | "advance";

export const FILTER_ORDER: ApprovalFilter[] = ["all", "leave", "overtime", "expense", "advance"];

/** Spec §5 dropdown metinleri (Tümü / İzin / Fazla mesai / Masraf / Avans) - kart gövdesindeki
 * `requestTypeLabel` ile aynı etiketler (QA-103: ikisi de tek `approvalTypeKey` eşlemesinden gelir). */
function filterLabel(filter: ApprovalFilter): string {
  return i18n.t(`approvals:filters.${filter}`);
}

/** `null` for a request type with no filter mapping yet (Requisition, AssetAssignment, ...) - such
 * a task is only ever counted/shown under "all" (brief: "bilinmeyen tür yalnız 'Tümü'de sayılır").
 * Uses the same `approvalTypeKey` normalizer as the card's type label (QA-103: "ExpenseClaim" must
 * count as "expense" here too, not just in the label). */
function filterOf(task: FilterableTask): ApprovalFilter | null {
  return approvalTypeKey(task.requestType);
}

export function countByFilter<T extends FilterableTask>(
  tasks: T[]
): Record<ApprovalFilter, number> {
  const counts: Record<ApprovalFilter, number> = {
    all: tasks.length,
    leave: 0,
    overtime: 0,
    expense: 0,
    advance: 0,
  };
  for (const task of tasks) {
    const filter = filterOf(task);
    if (filter) {
      counts[filter] += 1;
    }
  }
  return counts;
}

export function filterTasks<T extends FilterableTask>(tasks: T[], filter: ApprovalFilter): T[] {
  if (filter === "all") {
    return tasks;
  }
  return tasks.filter((task) => filterOf(task) === filter);
}

export type ApprovalFilterOption = { value: ApprovalFilter; label: string };

/** "Tür" alt sayfasındaki seçenek listesi - "İzin (1)" gibi sayılı etiketler (spec §5). */
export function approvalFilterOptions(
  counts: Record<ApprovalFilter, number>
): ApprovalFilterOption[] {
  return FILTER_ORDER.map((value) => ({
    value,
    label: i18n.t("approvals:filterWithCount", { label: filterLabel(value), count: counts[value] }),
  }));
}

/** "Tür" satırında gösterilen seçili değer metni - "Fazla mesai (1)" (chevron ayrı render edilir). */
export function approvalFilterSelectedText(
  filter: ApprovalFilter,
  counts: Record<ApprovalFilter, number>
): string {
  return i18n.t("approvals:filterWithCount", { label: filterLabel(filter), count: counts[filter] });
}
