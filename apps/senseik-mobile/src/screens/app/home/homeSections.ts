import type { LeaveBalance } from "@/services/LeaveService";
import { dateOnlyToDate, formatDate } from "@/lib/formatters";
import i18n from "@/lib/i18n";

/**
 * A home feed section's items, or `null` when the section must not render at all: its request
 * failed (e.g. 403 for a caller without that permission) or nothing survived `select`. Sections
 * load independently (`Promise.allSettled`), so one failure never hides the others.
 */
export function sectionFrom<T, R = unknown>(
  result: PromiseSettledResult<T[]>,
  select: (items: T[]) => R[]
): R[] | null {
  if (result.status === "rejected") return null;
  const items = select(result.value);
  return items.length > 0 ? items : null;
}

type BalanceFields = Pick<
  LeaveBalance,
  "carriedOverIntoPeriod" | "lastPeriodAccrued" | "extraGrants" | "adjustments" | "availableBalance"
>;

/**
 * "40,3 / 44 gün" on a balance card. `LeaveBalance` has no single "total" field (`totalAccrued` is a
 * lifetime sum), so the total is this period's entitlement - the positive terms of the server's own
 * `availableBalance` equation (see `LeaveService.ts`/`LeaveBalanceCard.tsx`): carried over +
 * accrued + extra grants + adjustments. When that is not positive there is no meaningful total and
 * the card shows "n gün kullanılabilir" without a bar (`entitlement: null`).
 */
export function balanceFraction(balance: BalanceFields): {
  available: number;
  entitlement: number | null;
  progress: number;
} {
  const entitlement =
    balance.carriedOverIntoPeriod +
    balance.lastPeriodAccrued +
    balance.extraGrants +
    balance.adjustments;
  if (entitlement <= 0) {
    return { available: balance.availableBalance, entitlement: null, progress: 0 };
  }
  const progress = Math.min(1, Math.max(0, balance.availableBalance / entitlement));
  return { available: balance.availableBalance, entitlement, progress };
}

/** "Bugün" on the day itself, "N gün kaldı" before it. */
export function birthdayCountdown(daysLeft: number): string {
  return daysLeft <= 0
    ? i18n.t("home:birthdays.today")
    : i18n.t("home:birthdays.daysLeft", { count: daysLeft });
}

const HTML_ENTITIES: Record<string, string> = {
  "&nbsp;": " ",
  "&amp;": "&",
  "&lt;": "<",
  "&gt;": ">",
  "&quot;": '"',
  "&#39;": "'",
};

/**
 * Announcement bodies are stored as rich-text HTML (the web renders them sanitized); the home card
 * only shows a short plain-text preview, so tags are dropped and the common entities decoded.
 */
export function plainTextPreview(html: string): string {
  return html
    .replace(/<[^>]*>/g, " ")
    .replace(/&(nbsp|amp|lt|gt|quot|#39);/g, (entity) => HTML_ENTITIES[entity] ?? entity)
    .replace(/\s+/g, " ")
    .trim();
}

/** "29 Eyl 2026" for a `DateOnly` ("yyyy-MM-dd"). */
export function formatShortDate(value: string): string {
  return formatDate(dateOnlyToDate(value), {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

/** Birthday pill: `{ day: "02", month: "Ekim" }` for "2026-10-02". */
export function dayAndMonth(value: string): { day: string; month: string } {
  const date = dateOnlyToDate(value);
  return {
    day: String(date.getDate()).padStart(2, "0"),
    month: formatDate(date, { month: "long" }),
  };
}
