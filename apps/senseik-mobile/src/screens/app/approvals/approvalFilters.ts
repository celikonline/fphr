import type { InboxTask } from "@/services/ApprovalsService";

/** Görev 8: "Onaylarım"'ın "Tür" seçimi - `InboxTask.requestType` sunucu değerlerinden türetilir. */
export type ApprovalFilter = "all" | "leave" | "overtime" | "expense" | "advance";

export const FILTER_ORDER: ApprovalFilter[] = ["all", "leave", "overtime", "expense", "advance"];

/** Spec §5 dropdown metinleri (Tümü / İzin / Fazla mesai / Masraf / Avans) - kart gövdesindeki
 * `requestTypeLabel`'dan bağımsız (orada "Expense" için "Harcama" kullanılıyor). */
export const FILTER_LABELS: Record<ApprovalFilter, string> = {
  all: "Tümü",
  leave: "İzin",
  overtime: "Fazla mesai",
  expense: "Masraf",
  advance: "Avans",
};

const REQUEST_TYPE_TO_FILTER: Record<string, ApprovalFilter | undefined> = {
  Leave: "leave",
  Overtime: "overtime",
  Expense: "expense",
  Advance: "advance",
};

/** `null` for a request type with no filter mapping yet (Requisition, AssetAssignment, ...) - such
 * a task is only ever counted/shown under "all" (brief: "bilinmeyen tür yalnız 'Tümü'de sayılır"). */
function filterOf(task: Pick<InboxTask, "requestType">): ApprovalFilter | null {
  return REQUEST_TYPE_TO_FILTER[task.requestType] ?? null;
}

export function countByFilter(tasks: InboxTask[]): Record<ApprovalFilter, number> {
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

export function filterTasks(tasks: InboxTask[], filter: ApprovalFilter): InboxTask[] {
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
    label: `${FILTER_LABELS[value]} (${counts[value]})`,
  }));
}

/** "Tür" satırında gösterilen seçili değer metni - "Fazla mesai (1)" (chevron ayrı render edilir). */
export function approvalFilterSelectedText(
  filter: ApprovalFilter,
  counts: Record<ApprovalFilter, number>
): string {
  return `${FILTER_LABELS[filter]} (${counts[filter]})`;
}
