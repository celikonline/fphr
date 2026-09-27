import { LEAVE_STATUS_LABELS, LEAVE_STATUS_TONES } from "@/components/leave/LeaveRequestListItem";
import { formatDayMonthYear, formatShortRange } from "@/components/form/dateRange";
import { formatCurrencyTRY, formatDays, formatDurationMinutes } from "@/lib/formatters";
import {
  ADVANCE_STATUS_LABELS,
  EXPENSE_CLAIM_STATUS_LABELS,
  expenseCategoryLabel,
} from "@/lib/expenseLabels";
import type { LeaveRequest, OvertimeRequest } from "@/services/LeaveService";
import type {
  AdvanceRequest,
  AdvanceRequestStatus,
  ExpenseCategory,
  ExpenseClaim,
  ExpenseClaimStatus,
} from "@/services/ExpenseService";
import type { Tone } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

export type RequestFilter = "all" | RequestKind;

export const REQUEST_FILTERS: { value: RequestFilter; label: string }[] = [
  { value: "all", label: "Tümü" },
  { value: "leave", label: "İzin" },
  { value: "overtime", label: "Fazla mesai" },
  { value: "expense", label: "Masraf" },
  { value: "advance", label: "Avans" },
];

/** One Taleplerim card, whatever the request type. `sortDate` is a `DateOnly` for ordering. */
export type RequestCard = {
  key: string;
  id: string;
  kind: RequestKind;
  title: string;
  dateLabel: string;
  detail: string;
  statusLabel: string;
  tone: Tone;
  description: string | null;
  /** Extra detail rows (a multi-item expense claim's items). */
  lines: string[];
  /** Only pending leave/overtime requests have a withdraw endpoint. */
  canWithdraw: boolean;
  sortDate: string;
};

const EXPENSE_CLAIM_STATUS_TONES: Record<ExpenseClaimStatus, Tone> = {
  draft: "neutral",
  submitted: "warning",
  approved: "success",
  rejected: "danger",
  paid: "success",
};

const ADVANCE_STATUS_TONES: Record<AdvanceRequestStatus, Tone> = {
  submitted: "warning",
  approved: "success",
  rejected: "danger",
  paid: "success",
};

export function leaveRequestCard(request: LeaveRequest): RequestCard {
  return {
    key: `leave-${request.id}`,
    id: request.id,
    kind: "leave",
    title: request.leaveTypeName ?? request.leaveTypeCode,
    dateLabel: formatShortRange(request.startDate, request.endDate),
    detail: `${formatDays(request.calculatedDays)} gün`,
    statusLabel: LEAVE_STATUS_LABELS[request.status],
    tone: LEAVE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: request.status === "pending",
    sortDate: request.startDate,
  };
}

export function overtimeRequestCard(request: OvertimeRequest): RequestCard {
  return {
    key: `overtime-${request.id}`,
    id: request.id,
    kind: "overtime",
    title: "Fazla mesai",
    dateLabel: `${formatDayMonthYear(request.workDate)} · ${request.startTime.slice(0, 5)}`,
    detail: formatDurationMinutes(request.durationMinutes),
    statusLabel: LEAVE_STATUS_LABELS[request.status],
    tone: LEAVE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: request.status === "pending",
    sortDate: request.workDate,
  };
}

type CategoryRef = Pick<ExpenseCategory, "id" | "code">;

function categoryName(categoryId: string, categories: CategoryRef[]): string {
  return expenseCategoryLabel(categories.find((c) => c.id === categoryId)?.code ?? categoryId);
}

export function expenseClaimCard(claim: ExpenseClaim, categories: CategoryRef[]): RequestCard {
  const first = claim.items[0];
  const knownCategory = first && categories.some((c) => c.id === first.categoryId);
  const receiptDate = first?.receiptDate ?? claim.submittedAt?.slice(0, 10);
  return {
    key: `expense-${claim.id}`,
    id: claim.id,
    kind: "expense",
    title: knownCategory ? categoryName(first.categoryId, categories) : "Masraf",
    dateLabel: receiptDate ? formatDayMonthYear(receiptDate) : "",
    detail: formatCurrencyTRY(claim.totalAmount),
    statusLabel: EXPENSE_CLAIM_STATUS_LABELS[claim.status],
    tone: EXPENSE_CLAIM_STATUS_TONES[claim.status],
    description: claim.description ?? null,
    lines:
      claim.items.length > 1
        ? claim.items.map(
            (item) =>
              `${categoryName(item.categoryId, categories)} · ${formatDayMonthYear(item.receiptDate)} · ${formatCurrencyTRY(item.amount)}`
          )
        : [],
    canWithdraw: false,
    sortDate: claim.submittedAt?.slice(0, 10) ?? receiptDate ?? "",
  };
}

export function advanceRequestCard(request: AdvanceRequest): RequestCard {
  return {
    key: `advance-${request.id}`,
    id: request.id,
    kind: "advance",
    title: "Avans",
    dateLabel: formatDayMonthYear(request.requestedDate),
    detail: formatCurrencyTRY(request.amount),
    statusLabel: ADVANCE_STATUS_LABELS[request.status],
    tone: ADVANCE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: false,
    sortDate: request.requestedDate,
  };
}

/** Every loaded request as one list, newest first (a source that failed to load is just `[]`). */
export function buildRequestCards(sources: {
  leave: LeaveRequest[];
  overtime: OvertimeRequest[];
  expense: ExpenseClaim[];
  advance: AdvanceRequest[];
  categories: CategoryRef[];
}): RequestCard[] {
  return [
    ...sources.leave.map(leaveRequestCard),
    ...sources.overtime.map(overtimeRequestCard),
    ...sources.expense.map((claim) => expenseClaimCard(claim, sources.categories)),
    ...sources.advance.map(advanceRequestCard),
  ].sort((a, b) => b.sortDate.localeCompare(a.sortDate));
}

export function filterRequestCards(cards: RequestCard[], filter: RequestFilter): RequestCard[] {
  return filter === "all" ? cards : cards.filter((card) => card.kind === filter);
}
