/**
 * Ödeme türü (formüllü kazanç) tanımları: mirrors `PayItemsController.cs` (`api/v1/payroll/pay-items`).
 * JSON is camelCase and null fields are omitted, so every optional field is `T | null | undefined` (missing = null).
 * Formulas are only ever validated by the server (`validate-formula`); there is no parser on the client.
 */
import { apiClient } from "@senseik/api-client";

const PAY_ITEMS_BASE = "/v1/payroll/pay-items";

export type PayItemDirection = "earning" | "deduction";
export type PayItemAmountBasis = "gross" | "net";

export interface PayItemDefinition {
  id: string;
  code: string;
  name: string;
  direction: PayItemDirection;
  amountBasis: PayItemAmountBasis;
  isSgkSubject?: boolean | null;
  isIncomeTaxSubject?: boolean | null;
  isStampTaxSubject?: boolean | null;
  formula?: string | null;
  defaultValue?: number | null;
  isActive?: boolean | null;
  /** YYYY-MM-DD */
  validFrom?: string | null;
  validTo?: string | null;
}

/** Body of `PUT /pay-items/{id}` (the code is immutable); `POST` adds `code`. Dates are YYYY-MM-DD. */
export interface PayItemInput {
  name: string;
  direction: PayItemDirection;
  amountBasis: PayItemAmountBasis;
  isSgkSubject: boolean;
  isIncomeTaxSubject: boolean;
  isStampTaxSubject: boolean;
  formula: string;
  defaultValue?: number | null;
  validFrom?: string | null;
  validTo?: string | null;
}

export interface CreatePayItemInput extends PayItemInput {
  code: string;
}

export interface FormulaVariable {
  name: string;
  sampleValue?: number | null;
}

export interface FormulaFunction {
  name: string;
  aliases?: string[] | null;
  minArgs?: number | null;
  maxArgs?: number | null;
}

export interface FormulaCatalog {
  variables?: FormulaVariable[] | null;
  functions?: FormulaFunction[] | null;
}

export interface FormulaValidationRequest {
  formula: string;
  amountBasis?: PayItemAmountBasis;
  sampleValues?: Record<string, number>;
}

export interface FormulaValidation {
  isValid: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  /** 1-based character position in the formula text. */
  position?: number | null;
  usedVariables?: string[] | null;
  sampleResult?: number | null;
}

export async function listPayItems(includeInactive: boolean): Promise<PayItemDefinition[]> {
  const { data } = await apiClient.get<PayItemDefinition[]>(PAY_ITEMS_BASE, { params: { includeInactive } });
  return data ?? [];
}

export async function createPayItem(input: CreatePayItemInput): Promise<string> {
  const { data } = await apiClient.post<string>(PAY_ITEMS_BASE, input);
  return data;
}

export async function updatePayItem(id: string, input: PayItemInput): Promise<void> {
  await apiClient.put(`${PAY_ITEMS_BASE}/${id}`, input);
}

export async function setPayItemActive(id: string, active: boolean): Promise<void> {
  await apiClient.post(`${PAY_ITEMS_BASE}/${id}/${active ? "activate" : "deactivate"}`);
}

export async function getFormulaCatalog(): Promise<FormulaCatalog> {
  const { data } = await apiClient.get<FormulaCatalog>(`${PAY_ITEMS_BASE}/formula-catalog`);
  return data ?? {};
}

export async function validatePayItemFormula(request: FormulaValidationRequest): Promise<FormulaValidation> {
  const { data } = await apiClient.post<FormulaValidation>(`${PAY_ITEMS_BASE}/validate-formula`, request);
  return data;
}

/**
 * Atamalar (`api/v1/payroll/pay-item-assignments`) ve dönem kalem satırları. Optional fields are `T | null | undefined` (missing = null).
 */
const PAY_ITEM_ASSIGNMENTS_BASE = "/v1/payroll/pay-item-assignments";

export interface PayItemAssignment {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  payItemDefinitionId: string;
  code: string;
  name?: string | null;
  value?: number | null;
  fromYear: number;
  fromMonth: number;
  toYear?: number | null;
  toMonth?: number | null;
  /** Definition direction; older servers omit it (derive from the definition list, default earning). */
  direction?: PayItemDirection | null;
}

export type PayItemAssignmentFilter = { employeeId: string; periodId?: undefined } | { periodId: string; employeeId?: undefined };

export interface CreatePayItemAssignmentsInput {
  payItemDefinitionId: string;
  employeeIds: string[];
  value?: number | null;
  fromYear: number;
  fromMonth: number;
  toYear?: number | null;
  toMonth?: number | null;
}

export interface UpdatePayItemAssignmentInput {
  value?: number | null;
  toYear?: number | null;
  toMonth?: number | null;
}

/** One calculated pay item line of a period (latest calculation trace). */
export interface PayItemLine {
  employeeId: string;
  employeeName?: string | null;
  code: string;
  name?: string | null;
  amount?: number | null;
  amountBasis?: PayItemAmountBasis | null;
  netTarget?: number | null;
  /** For a deduction line `amount` is the amount actually deducted. Missing = earning. */
  direction?: PayItemDirection | null;
}

export async function listPayItemAssignments(filter: PayItemAssignmentFilter): Promise<PayItemAssignment[]> {
  const { data } = await apiClient.get<PayItemAssignment[]>(PAY_ITEM_ASSIGNMENTS_BASE, { params: filter });
  return data ?? [];
}

export async function createPayItemAssignments(input: CreatePayItemAssignmentsInput): Promise<string[]> {
  const { data } = await apiClient.post<{ createdIds?: string[] | null }>(PAY_ITEM_ASSIGNMENTS_BASE, input);
  return data?.createdIds ?? [];
}

export async function updatePayItemAssignment(id: string, input: UpdatePayItemAssignmentInput): Promise<void> {
  await apiClient.put(`${PAY_ITEM_ASSIGNMENTS_BASE}/${id}`, input);
}

export async function deletePayItemAssignment(id: string): Promise<void> {
  await apiClient.delete(`${PAY_ITEM_ASSIGNMENTS_BASE}/${id}`);
}

export async function listPeriodPayItemLines(periodId: string): Promise<PayItemLine[]> {
  const { data } = await apiClient.get<PayItemLine[]>(`/v1/payroll/periods/${periodId}/pay-item-lines`);
  return data ?? [];
}
