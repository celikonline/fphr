/**
 * Teşvik tanımları (PAY-INCENTIVE-4): mirrors `IncentiveDefinitionsController.cs` and `Application/Incentives/IncentiveDefinitionFeatures.cs`
 * (`api/v1/payroll/incentive-definitions`). JSON is camelCase, enums are camelCase strings and null fields are omitted,
 * so every optional field is `T | null | undefined` (missing = null).
 */
import { apiClient } from "@senseik/api-client";

const INCENTIVE_DEFINITIONS_BASE = "/v1/payroll/incentive-definitions";

/** (a) employer SGK rate discount, (b) state covers premiums, (c) income tax withholding relief, (d) declaration code only. */
export type IncentiveKind = "employerSgkRateDiscount" | "employerShareCoverage" | "incomeTaxWithholdingRelief" | "declarationOnly";
/** Derived from the kind on the server: a, b, d = sgk; c = tax. */
export type IncentiveCategory = "sgk" | "tax";
export type IncentiveCoverageBase = "actualSgkBase" | "minimumWage";

/** A rate is either a literal `value` (0..1 fraction) XOR a `legislationKey` (`Payroll.` prefixed). */
export interface IncentiveRate {
  value?: number | null;
  legislationKey?: string | null;
}

export interface IncentiveParameters {
  rate?: IncentiveRate | null;
  coverageBase?: IncentiveCoverageBase | null;
  capMultiplier?: number | null;
  employerSgkRate?: IncentiveRate | null;
  coversEmployerSgk?: boolean | null;
  coversEmployerUnemployment?: boolean | null;
  coversEmployeeSgk?: boolean | null;
  coversEmployeeUnemployment?: boolean | null;
}

export interface IncentiveDefinition {
  id: string;
  code: string;
  name: string;
  lawReference?: string | null;
  declarationCode?: string | null;
  kind: IncentiveKind;
  category?: IncentiveCategory | null;
  parameters?: IncentiveParameters | null;
  maxMonths?: number | null;
  isSmmmApproved?: boolean | null;
  /** ISO date-time. */
  approvedAt?: string | null;
  approvedByUserId?: string | null;
  approvedByName?: string | null;
  approvalNote?: string | null;
  /** True when every field the kind needs is filled in (a precondition of approval). */
  isComplete?: boolean | null;
  isActive?: boolean | null;
  /** YYYY-MM-DD */
  validFrom?: string | null;
  validTo?: string | null;
  templateKey?: string | null;
}

/** Body of `PUT /incentive-definitions/{id}` (the code is immutable); `POST` adds `code`. Dates are YYYY-MM-DD; a missing `validTo` clears it. */
export interface IncentiveDefinitionInput {
  name: string;
  lawReference?: string | null;
  declarationCode?: string | null;
  kind: IncentiveKind;
  parameters?: IncentiveParameters | null;
  maxMonths?: number | null;
  validFrom?: string | null;
  validTo?: string | null;
}

export interface CreateIncentiveDefinitionInput extends IncentiveDefinitionInput {
  code: string;
}

export interface IncentiveTemplate {
  templateKey: string;
  code: string;
  name: string;
  lawReference?: string | null;
  kind: IncentiveKind;
  declarationCode?: string | null;
  preApproved?: boolean | null;
  alreadyAdded?: boolean | null;
}

export interface LegislationKeyValue {
  key: string;
  value?: number | null;
  /** YYYY-MM-DD */
  effectiveFrom?: string | null;
}

export interface IncentiveParameterCatalog {
  legislationKeys?: LegislationKeyValue[] | null;
}

export async function listIncentiveDefinitions(includeInactive: boolean): Promise<IncentiveDefinition[]> {
  const { data } = await apiClient.get<IncentiveDefinition[]>(INCENTIVE_DEFINITIONS_BASE, { params: { includeInactive } });
  return data ?? [];
}

export async function getIncentiveDefinition(id: string): Promise<IncentiveDefinition> {
  const { data } = await apiClient.get<IncentiveDefinition>(`${INCENTIVE_DEFINITIONS_BASE}/${id}`);
  return data;
}

export async function listIncentiveTemplates(): Promise<IncentiveTemplate[]> {
  const { data } = await apiClient.get<IncentiveTemplate[]>(`${INCENTIVE_DEFINITIONS_BASE}/templates`);
  return data ?? [];
}

export async function getIncentiveParameterCatalog(): Promise<IncentiveParameterCatalog> {
  const { data } = await apiClient.get<IncentiveParameterCatalog>(`${INCENTIVE_DEFINITIONS_BASE}/parameter-catalog`);
  return data ?? {};
}

export async function createIncentiveDefinition(input: CreateIncentiveDefinitionInput): Promise<string> {
  const { data } = await apiClient.post<string>(INCENTIVE_DEFINITIONS_BASE, input);
  return data;
}

/** Creates the definition from the in-code catalog; a second call for the same template is a 409. */
export async function createIncentiveFromTemplate(templateKey: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${INCENTIVE_DEFINITIONS_BASE}/from-template`, { templateKey });
  return data;
}

export async function updateIncentiveDefinition(id: string, input: IncentiveDefinitionInput): Promise<void> {
  await apiClient.put(`${INCENTIVE_DEFINITIONS_BASE}/${id}`, input);
}

export async function setIncentiveDefinitionActive(id: string, active: boolean): Promise<void> {
  await apiClient.post(`${INCENTIVE_DEFINITIONS_BASE}/${id}/${active ? "activate" : "deactivate"}`);
}

/** Declaration flag by the tenant's SMMM/expert (who, when and the note are recorded); needs `payroll.incentive.approve`. */
export async function approveIncentiveDefinition(id: string, note?: string | null): Promise<void> {
  await apiClient.post(`${INCENTIVE_DEFINITIONS_BASE}/${id}/approve`, { note: note || null });
}

export async function revokeIncentiveApproval(id: string, note?: string | null): Promise<void> {
  await apiClient.post(`${INCENTIVE_DEFINITIONS_BASE}/${id}/revoke-approval`, { note: note || null });
}
