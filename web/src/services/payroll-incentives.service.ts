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
  maxSupportMultiplier?: number | null;
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
  validFrom?: string | null;
  validTo?: string | null;
  maxMonths?: number | null;
  parameters?: IncentiveParameters | null;
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

/*
 * Teşvik atamaları ve eski profil kodu geçişi (PAY-INCENTIVE-5): mirrors `IncentiveAssignmentsController.cs`
 * (`api/v1/payroll/incentive-assignments`, `IncentiveAssignmentFeatures.cs`) and `IncentivesController.cs`
 * (`legacy-profile-codes`, `LegacyIncentiveCodeFeatures.cs`).
 */
const INCENTIVE_ASSIGNMENTS_BASE = "/v1/payroll/incentive-assignments";
const INCENTIVES_BASE = "/v1/payroll/incentives";

/** `IncentiveAssignmentDto`. */
export interface IncentiveAssignment {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  incentiveDefinitionId: string;
  code: string;
  name?: string | null;
  declarationCode?: string | null;
  kind: IncentiveKind;
  isSmmmApproved?: boolean | null;
  fromYear: number;
  fromMonth: number;
  toYear?: number | null;
  toMonth?: number | null;
  note?: string | null;
  isDefinitionActive?: boolean | null;
  /** YYYY-MM-DD */
  definitionValidFrom?: string | null;
  /** YYYY-MM-DD */
  definitionValidTo?: string | null;
}

/** Exactly one of `employeeId` / `periodId` is required by the server. */
export type IncentiveAssignmentFilter = { employeeId: string; periodId?: undefined } | { periodId: string; employeeId?: undefined };

/** `POST /incentive-assignments`: 1..500 employees, all-or-nothing. */
export interface CreateIncentiveAssignmentsInput {
  incentiveDefinitionId: string;
  employeeIds: string[];
  fromYear: number;
  fromMonth: number;
  toYear?: number | null;
  toMonth?: number | null;
  note?: string | null;
}

/** `PUT /incentive-assignments/{id}`: the server overwrites the note, so send the existing one to keep it. */
export interface UpdateIncentiveAssignmentInput {
  toYear?: number | null;
  toMonth?: number | null;
  note?: string | null;
}

/** `LegacyIncentiveCodeDto`: one distinct profile incentive code (raw, grouped as typed) with the matching definition. */
export interface LegacyProfileCode {
  code: string;
  /** Digits only, left-padded with zeros to 5 ("5510" -> "05510"). */
  normalizedCode: string;
  employeeCount: number;
  suggestedDefinitionId?: string | null;
  suggestedDefinitionCode?: string | null;
  suggestedIsApproved?: boolean | null;
  /** True when converting to the suggested definition would change the calculation (the flat 5 points is lost). */
  calculationChanges?: boolean | null;
}

export interface ConvertLegacyProfileCodeInput {
  code: string;
  incentiveDefinitionId: string;
  fromYear: number;
  fromMonth: number;
  toYear?: number | null;
  toMonth?: number | null;
}

export interface ConvertLegacyProfileCodeResult {
  createdIds: string[];
  skippedEmployeeIds: string[];
}

export async function listIncentiveAssignments(filter: IncentiveAssignmentFilter): Promise<IncentiveAssignment[]> {
  const { data } = await apiClient.get<IncentiveAssignment[]>(INCENTIVE_ASSIGNMENTS_BASE, { params: filter });
  return data ?? [];
}

export async function createIncentiveAssignments(input: CreateIncentiveAssignmentsInput): Promise<string[]> {
  const { data } = await apiClient.post<{ createdIds?: string[] | null }>(INCENTIVE_ASSIGNMENTS_BASE, input);
  return data?.createdIds ?? [];
}

export async function updateIncentiveAssignment(id: string, input: UpdateIncentiveAssignmentInput): Promise<void> {
  await apiClient.put(`${INCENTIVE_ASSIGNMENTS_BASE}/${id}`, input);
}

export async function deleteIncentiveAssignment(id: string): Promise<void> {
  await apiClient.delete(`${INCENTIVE_ASSIGNMENTS_BASE}/${id}`);
}

/** Migration preview (`payroll.incentive.manage`); `asOf` is YYYY-MM-DD (server default: today). */
export async function listLegacyProfileCodes(asOf?: string): Promise<LegacyProfileCode[]> {
  const { data } = await apiClient.get<LegacyProfileCode[]>(`${INCENTIVES_BASE}/legacy-profile-codes`, { params: asOf ? { asOf } : undefined });
  return data ?? [];
}

export async function convertLegacyProfileCode(input: ConvertLegacyProfileCodeInput): Promise<ConvertLegacyProfileCodeResult> {
  const { data } = await apiClient.post<{ createdIds?: string[] | null; skippedEmployeeIds?: string[] | null }>(
    `${INCENTIVES_BASE}/legacy-profile-codes/convert`,
    input,
  );
  return { createdIds: data?.createdIds ?? [], skippedEmployeeIds: data?.skippedEmployeeIds ?? [] };
}
