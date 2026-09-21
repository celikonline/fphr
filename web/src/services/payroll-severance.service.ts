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

/** `ExitSettlementDto`: the amounts frozen when the employee was terminated (`ExitSettlementController`). */
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
  calculatedAt: string;
  steps: SeveranceTraceStep[];
}

/** The employee's latest exit settlement, or null when none is recorded (404). */
export async function getExitSettlement(employeeId: string): Promise<ExitSettlement | null> {
  const { data } = await apiClient.get<ExitSettlement>(`${PAYROLL_EMPLOYEES_BASE}/${employeeId}/exit-settlement`, {
    validateStatus: (status) => status === 200 || status === 404,
  });
  return data && "totalAmount" in data ? data : null;
}
