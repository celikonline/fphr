import { LEAVE_STATUS_TONES, leaveStatusLabel } from "@/components/leave/LeaveRequestListItem";
import { formatDayMonthYear, formatShortRange } from "@/components/form/dateRange";
import {
  formatCurrencyTRY,
  formatDays,
  formatDurationMinutes,
  toLocalDateOnly,
} from "@/lib/formatters";
import {
  advanceStatusLabel,
  expenseClaimStatusLabel,
  expenseCategoryLabel,
} from "@/lib/expenseLabels";
import i18n from "@/lib/i18n";
import type { LeaveRequest, OvertimeRequest } from "@/services/LeaveService";
import type {
  AdvanceRequest,
  AdvanceRequestStatus,
  ExpenseCategory,
  ExpenseClaim,
  ExpenseClaimStatus,
} from "@/services/ExpenseService";
import type { PunchCorrectionRequest, PunchCorrectionStatus } from "@/services/AttendanceService";
import type { Tone } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

export type RequestFilter = "all" | RequestKind;

/** Taleplerim's type filter; `labelKey` is an i18n key - render with `t(filter.labelKey)`. */
export const REQUEST_FILTERS: { value: RequestFilter; labelKey: string }[] = [
  { value: "all", labelKey: "requests:filters.all" },
  { value: "leave", labelKey: "requests:options.leave.label" },
  { value: "overtime", labelKey: "requests:options.overtime.label" },
  { value: "expense", labelKey: "requests:options.expense.label" },
  { value: "advance", labelKey: "requests:options.advance.label" },
  { value: "punchCorrection", labelKey: "requests:options.punchCorrection.label" },
];

/** One Taleplerim card, whatever the request type. `sortDate` is the local `DateOnly` the request
 * was submitted on (see `submittedDay`), falling back to the type's own date when the DTO has no
 * submission timestamp - so every kind is ordered by the same thing. */
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
  /** Submission instant in epoch ms - orders same-day cards; 0 when the DTO has no timestamp. */
  sortInstant: number;
};

/** Local calendar day of a UTC submission instant (never the UTC day - "2026-09-26T22:30:00Z" is
 * the 27th in Istanbul); undefined when the DTO carries no (valid) timestamp. */
function submittedDay(submittedAt: string | null | undefined): string | undefined {
  const instant = submittedInstant(submittedAt);
  return instant ? toLocalDateOnly(new Date(instant)) : undefined;
}

function submittedInstant(submittedAt: string | null | undefined): number {
  if (!submittedAt) return 0;
  const ms = new Date(submittedAt).getTime();
  return Number.isNaN(ms) ? 0 : ms;
}

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
    detail: i18n.t("common:units.days", {
      count: request.calculatedDays,
      value: formatDays(request.calculatedDays),
    }),
    statusLabel: leaveStatusLabel(request.status),
    tone: LEAVE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: request.status === "pending",
    sortDate: submittedDay(request.submittedAt) ?? request.startDate,
    sortInstant: submittedInstant(request.submittedAt),
  };
}

export function overtimeRequestCard(request: OvertimeRequest): RequestCard {
  return {
    key: `overtime-${request.id}`,
    id: request.id,
    kind: "overtime",
    title: i18n.t("requests:options.overtime.label"),
    dateLabel: `${formatDayMonthYear(request.workDate)} · ${request.startTime.slice(0, 5)}`,
    detail: formatDurationMinutes(request.durationMinutes),
    statusLabel: leaveStatusLabel(request.status),
    tone: LEAVE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: request.status === "pending",
    sortDate: submittedDay(request.submittedAt) ?? request.workDate,
    sortInstant: submittedInstant(request.submittedAt),
  };
}

type CategoryRef = Pick<ExpenseCategory, "id" | "code">;

function categoryName(categoryId: string, categories: CategoryRef[]): string {
  return expenseCategoryLabel(categories.find((c) => c.id === categoryId)?.code ?? categoryId);
}

export function expenseClaimCard(claim: ExpenseClaim, categories: CategoryRef[]): RequestCard {
  const first = claim.items[0];
  const knownCategory = first && categories.some((c) => c.id === first.categoryId);
  const submissionDay = submittedDay(claim.submittedAt);
  const receiptDate = first?.receiptDate ?? submissionDay;
  return {
    key: `expense-${claim.id}`,
    id: claim.id,
    kind: "expense",
    title: knownCategory
      ? categoryName(first.categoryId, categories)
      : i18n.t("requests:options.expense.label"),
    dateLabel: receiptDate ? formatDayMonthYear(receiptDate) : "",
    detail: formatCurrencyTRY(claim.totalAmount),
    statusLabel: expenseClaimStatusLabel(claim.status),
    tone: EXPENSE_CLAIM_STATUS_TONES[claim.status],
    // TK-031: mobile now creates the draft without a claim-level description and puts the text on
    // each item, so a single-item claim shows its item's description instead.
    description: claim.description ?? (claim.items.length === 1 ? first.description : null) ?? null,
    lines:
      claim.items.length > 1
        ? claim.items.map((item) =>
            [
              categoryName(item.categoryId, categories),
              formatDayMonthYear(item.receiptDate),
              formatCurrencyTRY(item.amount),
              item.description,
            ]
              .filter(Boolean)
              .join(" · ")
          )
        : [],
    canWithdraw: false,
    sortDate: submissionDay ?? receiptDate ?? "",
    sortInstant: submittedInstant(claim.submittedAt),
  };
}

export function advanceRequestCard(request: AdvanceRequest): RequestCard {
  return {
    key: `advance-${request.id}`,
    id: request.id,
    kind: "advance",
    title: i18n.t("requests:options.advance.label"),
    dateLabel: formatDayMonthYear(request.requestedDate),
    detail: formatCurrencyTRY(request.amount),
    statusLabel: advanceStatusLabel(request.status),
    tone: ADVANCE_STATUS_TONES[request.status],
    description: request.description ?? null,
    lines: [],
    canWithdraw: false,
    // `AdvanceRequestDto` has no submission timestamp; `requestedDate` is the day it was requested.
    sortDate: request.requestedDate,
    sortInstant: 0,
  };
}

const PUNCH_CORRECTION_STATUS_TONES: Record<PunchCorrectionStatus, Tone> = {
  pending: "warning",
  approved: "success",
  applyFailed: "danger",
  rejected: "danger",
  withdrawn: "neutral",
};

function punchCorrectionStatusLabel(status: PunchCorrectionStatus): string {
  return i18n.t(`requests:punchCorrectionStatus.${status}`);
}

export function punchCorrectionRequestCard(request: PunchCorrectionRequest): RequestCard {
  const times = [request.requestedCheckIn, request.requestedCheckOut]
    .filter((value): value is string => Boolean(value))
    .map((value) => value.slice(0, 5))
    .join(" – ");
  return {
    key: `punchCorrection-${request.id}`,
    id: request.id,
    kind: "punchCorrection",
    title: i18n.t("requests:options.punchCorrection.label"),
    dateLabel: formatDayMonthYear(request.date),
    detail: times,
    statusLabel: punchCorrectionStatusLabel(request.status),
    tone: PUNCH_CORRECTION_STATUS_TONES[request.status],
    description: request.reason,
    lines: [],
    canWithdraw: request.status === "pending",
    sortDate: submittedDay(request.submittedAt) ?? request.date,
    sortInstant: submittedInstant(request.submittedAt),
  };
}

/** Every loaded request as one list, most recently submitted first (a source that failed to load
 * is just `[]`). Same-day cards go by submission time; those without one keep list order. */
export function buildRequestCards(sources: {
  leave: LeaveRequest[];
  overtime: OvertimeRequest[];
  expense: ExpenseClaim[];
  advance: AdvanceRequest[];
  /** Optional so existing callers/tests built before FG-004 keep working unchanged. */
  punchCorrection?: PunchCorrectionRequest[];
  categories: CategoryRef[];
}): RequestCard[] {
  return [
    ...sources.leave.map(leaveRequestCard),
    ...sources.overtime.map(overtimeRequestCard),
    ...sources.expense.map((claim) => expenseClaimCard(claim, sources.categories)),
    ...sources.advance.map(advanceRequestCard),
    ...(sources.punchCorrection ?? []).map(punchCorrectionRequestCard),
  ].sort((a, b) => b.sortDate.localeCompare(a.sortDate) || b.sortInstant - a.sortInstant);
}

export function filterRequestCards(cards: RequestCard[], filter: RequestFilter): RequestCard[] {
  return filter === "all" ? cards : cards.filter((card) => card.kind === filter);
}
