/**
 * Expense module (Faz 4 Ex0): mirrors `ExpenseClaimsController`/`AdvancesController`/`PaymentsController`/
 * `ExpenseCategoriesController.cs` (`api/v1/expense/{claims,advances,payments,categories}`). Ex0 scope only:
 * self-service claim/advance creation + submit, combined payments overview. No approve/pay actions here - those
 * happen through Workflow (see docs/analysis/kolayik-video/faz4-durum-takibi.md "Ex0").
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
  const { data } = await apiClient.post<string>(`${EXPENSE_BASE}/claims`, { description: description ?? null });
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

export interface ExpenseCategory {
  id: string;
  code: string;
  nameKey: string;
  defaultTaxRate: number | null;
  isActive: boolean;
}

export async function listExpenseCategories(includeInactive = false): Promise<ExpenseCategory[]> {
  const { data } = await apiClient.get<ExpenseCategory[]>(`${EXPENSE_BASE}/categories`, { params: { includeInactive } });
  return data;
}
