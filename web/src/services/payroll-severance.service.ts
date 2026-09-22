/**
 * Employee card "İşten ayrılış sihirbazı" > read-only özet blok (senseik-011, F-06): mirrors
 * `SeveranceEstimateController` (`api/v1/payroll/employees/{id}/severance-estimate`). Nothing is persisted - a
 * hypothetical exit date/cause the caller is still deciding on; gated by payroll.severance.estimate.
 */
import { apiClient } from "@senseik/api-client";

const PAYROLL_EMPLOYEES_BASE = "/v1/payroll/employees";

/** Mirrors `SeveranceCauseCategory` (camelCase enum JSON); coarser than `TerminationType`, see lifecycle-actions.tsx's mapping. */
export type SeveranceCauseCategory =
  | "employerNoticeBased"
  | "employeeJustCause"
  | "employerJustCauseOther"
  | "immoralOrBadFaith"
  | "employeeResignation";

/** `SeveranceTraceStepDto`; display-only, mirrors the payslip trace step shape. */
export interface SeveranceTraceStep {
  order: number;
  code: string;
  titleKey: string;
  inputs: Record<string, string>;
  formula: string;
  output: number;
}

/** `SeveranceEstimateDto`. */
export interface SeveranceEstimate {
  severanceAmount: number;
  noticeAmount: number;
  unusedLeaveAmount: number;
  totalAmount: number;
  seniorityDays: number;
  dailyGrossWage: number;
  currency: string;
  steps: SeveranceTraceStep[];
}

export async function getSeveranceEstimate(
  employeeId: string,
  terminationDate: string,
  causeCategory: SeveranceCauseCategory,
): Promise<SeveranceEstimate> {
  const { data } = await apiClient.get<SeveranceEstimate>(`${PAYROLL_EMPLOYEES_BASE}/${employeeId}/severance-estimate`, {
    params: { terminationDate, causeCategory },
  });
  return data;
}

/** Mirrors `ExitPayKind` (camelCase enum JSON). */
export type ExitPayKind =
  | "severance"
  | "notice"
  | "unusedLeave"
  | "mutualRescissionPay"
  | "extraPayment"
  | "finalMonthWage"
  | "other";

/** Kinds a user can add by hand; severance/notice/unusedLeave lines come from the exit event and can only be edited. */
export const MANUAL_EXIT_PAY_KINDS: ExitPayKind[] = ["mutualRescissionPay", "extraPayment", "finalMonthWage", "other"];

/** `ExitSettlementLineDto`: one payment line with the deductions it was netted with. */
export interface ExitSettlementLine {
  id: string;
  kind: ExitPayKind;
  label: string;
  grossAmount: number;
  sgkSubject: boolean;
  incomeTaxSubject: boolean;
  stampTaxSubject: boolean;
  isAutomatic: boolean;
  sgkAmount: number;
  incomeTaxAmount: number;
  stampTaxAmount: number;
  netAmount: number;
}

/** `ExitSettlementDto`: the exit settlement as recorded at exit plus any lines added since, with the net. */
export interface ExitSettlement {
  id: string;
  employeeId: string;
  terminationDate: string;
  causeCategory: SeveranceCauseCategory;
  currency: string;
  seniorityDays: number;
  dailyGrossWage: number;
  severanceAmount: number;
  noticeAmount: number;
  unusedLeaveAmount: number;
  totalAmount: number;
  netCalculated: boolean;
  sgkDeduction: number;
  incomeTax: number;
  stampTax: number;
  netTotal: number;
  calculatedAt: string;
  lines: ExitSettlementLine[];
  steps: SeveranceTraceStep[];
}

/** Body of a line add/edit. */
export interface ExitSettlementLineInput {
  label: string;
  grossAmount: number;
  sgkSubject: boolean;
  incomeTaxSubject: boolean;
  stampTaxSubject: boolean;
}

const EXIT_SETTLEMENTS_BASE = "/v1/payroll/exit-settlements";

/** The employee's latest exit settlement, or null when none is recorded (404). */
export async function getExitSettlement(employeeId: string): Promise<ExitSettlement | null> {
  const { data } = await apiClient.get<ExitSettlement>(`${PAYROLL_EMPLOYEES_BASE}/${employeeId}/exit-settlement`, {
    validateStatus: (status) => status === 200 || status === 404,
  });
  return data && "totalAmount" in data ? data : null;
}

export async function addExitSettlementLine(settlementId: string, kind: ExitPayKind, input: ExitSettlementLineInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${EXIT_SETTLEMENTS_BASE}/${settlementId}/lines`, { kind, ...input });
  return data;
}

export async function updateExitSettlementLine(settlementId: string, lineId: string, input: ExitSettlementLineInput): Promise<void> {
  await apiClient.put(`${EXIT_SETTLEMENTS_BASE}/${settlementId}/lines/${lineId}`, input);
}

export async function removeExitSettlementLine(settlementId: string, lineId: string): Promise<void> {
  await apiClient.delete(`${EXIT_SETTLEMENTS_BASE}/${settlementId}/lines/${lineId}`);
}

export async function recalculateExitSettlement(settlementId: string): Promise<void> {
  await apiClient.post(`${EXIT_SETTLEMENTS_BASE}/${settlementId}/recalculate`);
}
