/**
 * Expense module: mirrors `ExpenseClaimsController`/`AdvancesController`/`PaymentsController`/
 * `ExpenseCategoriesController.cs` (`api/v1/expense/{claims,advances,payments,categories}`). Self-service
 * claim/advance creation + submit, combined payments overview, plus the HR/Finance worklist (tenant-wide list +
 * "mark as paid") added in Faz 7 - approval itself still happens through Workflow.
 */
import { apiClient } from "@senseik/api-client";

const EXPENSE_BASE = "/v1/expense";

export type ExpenseClaimStatus = "draft" | "submitted" | "approved" | "rejected" | "paid";
export type AdvanceRequestStatus = "submitted" | "approved" | "rejected" | "paid";

export interface ExpenseItem {
  id: string;
  categoryId: string;
  amount: number;
  receiptDate: string;
  taxRate: number | null;
  taxAmount: number;
  currency: string;
  receiptFileId: string | null;
  description: string | null;
  /** S-9 (Faz 7 tur 3): true when `amount` exceeds the category's `maxAmount`; a warning, not a submit blocker. Absent on older API responses. */
  exceedsCategoryLimit?: boolean;
}

export interface ExpenseClaim {
  id: string;
  employeeId: string;
  description: string | null;
  totalAmount: number;
  status: ExpenseClaimStatus;
  approvalRequestId: string | null;
  submittedAt: string | null;
  decidedAt: string | null;
  paidAt: string | null;
  paidByEmployeeId: string | null;
  paymentNote: string | null;
  items: ExpenseItem[];
}

export interface AddExpenseItemInput {
  categoryId: string;
  amount: number;
  receiptDate: string;
  taxRate?: number | null;
  currency: string;
  receiptFileId?: string | null;
  description?: string | null;
}

export async function listMyExpenseClaims(): Promise<ExpenseClaim[]> {
  const { data } = await apiClient.get<ExpenseClaim[]>(`${EXPENSE_BASE}/claims/my`);
  return data;
}

export async function getExpenseClaim(id: string): Promise<ExpenseClaim> {
  const { data } = await apiClient.get<ExpenseClaim>(`${EXPENSE_BASE}/claims/${id}`);
  return data;
}

export async function createExpenseClaim(description?: string | null): Promise<string> {
  const { data } = await apiClient.post<string>(`${EXPENSE_BASE}/claims`, {
    description: description ?? null,
  });
  return data;
}

export async function addExpenseItem(claimId: string, input: AddExpenseItemInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${EXPENSE_BASE}/claims/${claimId}/items`, input);
  return data;
}

export async function removeExpenseItem(claimId: string, itemId: string): Promise<void> {
  await apiClient.delete(`${EXPENSE_BASE}/claims/${claimId}/items/${itemId}`);
}

export async function submitExpenseClaim(claimId: string): Promise<void> {
  await apiClient.post(`${EXPENSE_BASE}/claims/${claimId}/submit`);
}

/** HR/Finance worklist: tenant-wide non-Draft claims, optionally filtered to one status (e.g. "approved", awaiting pay). */
export async function listExpenseClaims(status?: ExpenseClaimStatus): Promise<ExpenseClaim[]> {
  const { data } = await apiClient.get<ExpenseClaim[]>(`${EXPENSE_BASE}/claims`, {
    params: status ? { status } : undefined,
  });
  return data;
}

/** HR/Finance marks an Approved claim as paid. */
export async function payExpenseClaim(claimId: string, paymentNote?: string | null): Promise<void> {
  await apiClient.post(`${EXPENSE_BASE}/claims/${claimId}/pay`, {
    paymentNote: paymentNote ?? null,
  });
}

export interface AdvanceRequest {
  id: string;
  employeeId: string;
  amount: number;
  currency: string;
  requestedDate: string;
  isInstallmentRequested: boolean;
  installmentCount: number | null;
  description: string | null;
  status: AdvanceRequestStatus;
  approvalRequestId: string | null;
  decidedAt: string | null;
  paidAt: string | null;
  paidByEmployeeId: string | null;
  paymentNote: string | null;
}

export interface CreateAdvanceRequestInput {
  amount: number;
  currency: string;
  requestedDate?: string | null;
  isInstallmentRequested: boolean;
  installmentCount?: number | null;
  description?: string | null;
}

export async function listMyAdvanceRequests(): Promise<AdvanceRequest[]> {
  const { data } = await apiClient.get<AdvanceRequest[]>(`${EXPENSE_BASE}/advances/my`);
  return data;
}

export async function createAdvanceRequest(input: CreateAdvanceRequestInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${EXPENSE_BASE}/advances`, input);
  return data;
}

/** HR/Finance worklist: tenant-wide advance requests, optionally filtered to one status (e.g. "approved", awaiting pay). */
export async function listAdvanceRequests(
  status?: AdvanceRequestStatus
): Promise<AdvanceRequest[]> {
  const { data } = await apiClient.get<AdvanceRequest[]>(`${EXPENSE_BASE}/advances`, {
    params: status ? { status } : undefined,
  });
  return data;
}

/** HR/Finance marks an Approved advance request as paid. */
export async function payAdvanceRequest(
  requestId: string,
  paymentNote?: string | null
): Promise<void> {
  await apiClient.post(`${EXPENSE_BASE}/advances/${requestId}/pay`, {
    paymentNote: paymentNote ?? null,
  });
}

export type PaymentKind = "expense" | "advance" | "overtime" | "adjustment";

export interface PaymentOverviewRow {
  kind: PaymentKind;
  date: string;
  amount: number;
  currency: string;
  status: string;
  sourceId: string;
  description: string | null;
}

export async function listMyPayments(): Promise<PaymentOverviewRow[]> {
  const { data } = await apiClient.get<PaymentOverviewRow[]>(`${EXPENSE_BASE}/payments/my`);
  return data;
}

/** Employee card "Ödemeler" tab (HR/Finance): the same overview, scoped to one employee. */
export async function getEmployeePayments(employeeId: string): Promise<PaymentOverviewRow[]> {
  const { data } = await apiClient.get<PaymentOverviewRow[]>(
    `${EXPENSE_BASE}/payments/employee/${employeeId}`
  );
  return data;
}

export interface ExpenseCategory {
  id: string;
  code: string;
  nameKey: string;
  /** Omitted by the API when null (`WhenWritingNull`). */
  defaultTaxRate?: number | null;
  isActive: boolean;
  /** S-9 (Faz 7 tur 3): optional per-item cap; `null`/absent = no limit. Absent on older API responses. */
  maxAmount?: number | null;
}

export async function listExpenseCategories(includeInactive = false): Promise<ExpenseCategory[]> {
  const { data } = await apiClient.get<ExpenseCategory[]>(`${EXPENSE_BASE}/categories`, {
    params: { includeInactive },
  });
  return data;
}

export interface CreateExpenseCategoryInput {
  code: string;
  nameKey: string;
  defaultTaxRate?: number | null;
  maxAmount?: number | null;
}

/** HR/Finance (`expense.categories.manage`) adds a new category to the tenant's catalog. */
export async function createExpenseCategory(input: CreateExpenseCategoryInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${EXPENSE_BASE}/categories`, {
    code: input.code,
    nameKey: input.nameKey,
    defaultTaxRate: input.defaultTaxRate ?? null,
    maxAmount: input.maxAmount ?? null,
  });
  return data;
}

/** HR/Finance (`expense.categories.manage`) activates or deactivates a category. */
export async function setExpenseCategoryActive(
  categoryId: string,
  isActive: boolean
): Promise<void> {
  await apiClient.put(`${EXPENSE_BASE}/categories/${categoryId}/active`, { isActive });
}

export interface UpdateExpenseCategoryInput {
  name: string;
  maxAmount?: number | null;
}

/**
 * HR/Finance (`expense.categories.manage`) renames a category and/or sets its limit (S-9 `PUT /categories/{id}`).
 * System-seeded categories reject a name change with 409 `expense.category.system_immutable` (server-localized message).
 */
export async function updateExpenseCategory(
  categoryId: string,
  input: UpdateExpenseCategoryInput
): Promise<void> {
  await apiClient.put(`${EXPENSE_BASE}/categories/${categoryId}`, {
    name: input.name,
    maxAmount: input.maxAmount ?? null,
  });
}
