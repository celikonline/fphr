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
