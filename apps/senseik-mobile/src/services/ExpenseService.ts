import { apiClient } from "@/lib/apiClient";

/**
 * TK-031: Expense module (self-service create/submit only) - mirrors `AdvancesController.cs`,
 * `ExpenseClaimsController.cs` and `ExpenseCategoriesController.cs`'s `/expense/{advances,claims,categories}`
 * groups, the same endpoints `web/src/services/expense.service.ts` calls. Mobile base path is
 * `/api/v1/...` (web uses `/v1/...` via its own `apiClient` baseURL) - do not copy web's `EXPENSE_BASE`
 * verbatim. Only the caller's own advances/claims (`*.my.*` permissions); HR/Finance worklist, pay,
 * and category management stay web-only.
 */
const EXPENSE_BASE = "/api/v1/expense";

/** Mirrors `ExpenseLimits.PayrollCurrency` (`ExpenseDomainErrors.cs:56`) - the only currency payroll reimburses. */
export const EXPENSE_DEFAULT_CURRENCY = "TRY";

export type ExpenseClaimStatus = "draft" | "submitted" | "approved" | "rejected" | "paid";
export type AdvanceRequestStatus = "submitted" | "approved" | "rejected" | "paid";

/** `ExpenseCategoryDto`. */
export interface ExpenseCategory {
  id: string;
  code: string;
  nameKey: string;
  defaultTaxRate?: number | null;
  isActive: boolean;
  maxAmount?: number | null;
}

/** `ExpenseItemDto`. */
export interface ExpenseItem {
  id: string;
  categoryId: string;
  amount: number;
  receiptDate: string;
  taxRate?: number | null;
  taxAmount: number;
  currency: string;
  receiptFileId?: string | null;
  description?: string | null;
  /** True when `amount` exceeds the category's `maxAmount` - a warning, not a submit blocker. */
  exceedsCategoryLimit?: boolean;
}

/** `ExpenseClaimDto`. */
export interface ExpenseClaim {
  id: string;
  employeeId: string;
  description?: string | null;
  totalAmount: number;
  status: ExpenseClaimStatus;
  approvalRequestId?: string | null;
  submittedAt?: string | null;
  decidedAt?: string | null;
  paidAt?: string | null;
  paidByEmployeeId?: string | null;
  paymentNote?: string | null;
  items: ExpenseItem[];
}

/** `AddExpenseItemRequest` body - v1 is single-item, so `receiptFileId` is never sent (no photo upload yet). */
export interface AddExpenseItemInput {
  categoryId: string;
  amount: number;
  receiptDate: string;
  taxRate?: number | null;
  currency: string;
  description?: string | null;
}

/** `AdvanceRequestDto`. */
export interface AdvanceRequest {
  id: string;
  employeeId: string;
  amount: number;
  currency: string;
  requestedDate: string;
  isInstallmentRequested: boolean;
  installmentCount?: number | null;
  description?: string | null;
  status: AdvanceRequestStatus;
  approvalRequestId?: string | null;
  decidedAt?: string | null;
  paidAt?: string | null;
  paidByEmployeeId?: string | null;
  paymentNote?: string | null;
}

/** `CreateAdvanceRequestRequest` body. */
export interface CreateAdvanceRequestInput {
  amount: number;
  currency: string;
  requestedDate?: string | null;
  isInstallmentRequested: boolean;
  installmentCount?: number | null;
  description?: string | null;
}

/**
 * Thrown by `submitSingleItemExpenseClaim` when the draft claim was created but a later step (add
 * item / submit) failed. `ExpenseClaimsController.cs` has no claim-level `DELETE` (only a
 * per-item one), so a half-finished draft can't be cleaned up from mobile - the caller must tell
 * the user the draft exists and point them at the web app to finish or discard it, never fail
 * silently and leave an orphaned draft the user doesn't know about.
 */
export class ExpenseClaimDraftIncompleteError extends Error {
  constructor(
    public readonly claimId: string,
    public readonly cause: unknown
  ) {
    super("Expense claim draft was created but a later step failed.");
    this.name = "ExpenseClaimDraftIncompleteError";
  }
}

export class ExpenseService {
  async getCategories(): Promise<ExpenseCategory[]> {
    const response = await apiClient.get<ExpenseCategory[]>(`${EXPENSE_BASE}/categories`);
    return response.data;
  }

  async getMyExpenseClaims(): Promise<ExpenseClaim[]> {
    const response = await apiClient.get<ExpenseClaim[]>(`${EXPENSE_BASE}/claims/my`);
    return response.data;
  }

  private async createExpenseClaimDraft(description: string | null): Promise<string> {
    const response = await apiClient.post<string>(`${EXPENSE_BASE}/claims`, { description });
    return response.data;
  }

  private async addExpenseItem(claimId: string, input: AddExpenseItemInput): Promise<string> {
    const response = await apiClient.post<string>(`${EXPENSE_BASE}/claims/${claimId}/items`, input);
    return response.data;
  }

  private async submitExpenseClaim(claimId: string): Promise<void> {
    await apiClient.post(`${EXPENSE_BASE}/claims/${claimId}/submit`);
  }

  /**
   * v1 single-item flow (senseik-mobile TK-031 decision #2): draft -> add the one item -> submit.
   * If adding the item or submitting fails, throws `ExpenseClaimDraftIncompleteError` carrying the
   * already-created `claimId` instead of swallowing the error - see that class's doc comment for why
   * mobile can't just delete the draft itself.
   */
  async submitSingleItemExpenseClaim(
    input: AddExpenseItemInput & { description?: string | null }
  ): Promise<string> {
    const claimId = await this.createExpenseClaimDraft(input.description ?? null);
    try {
      await this.addExpenseItem(claimId, input);
      await this.submitExpenseClaim(claimId);
      return claimId;
    } catch (error) {
      throw new ExpenseClaimDraftIncompleteError(claimId, error);
    }
  }

  async getMyAdvanceRequests(): Promise<AdvanceRequest[]> {
    const response = await apiClient.get<AdvanceRequest[]>(`${EXPENSE_BASE}/advances/my`);
    return response.data;
  }

  async createAdvanceRequest(input: CreateAdvanceRequestInput): Promise<string> {
    const response = await apiClient.post<string>(`${EXPENSE_BASE}/advances`, input);
    return response.data;
  }
}

export const expenseService = new ExpenseService();
