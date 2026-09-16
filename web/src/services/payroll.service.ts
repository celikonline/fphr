/**
 * Payroll module (Faz 7 tur 3, read-only web slice): mirrors `PayrollPeriodsController.cs`
 * (`api/v1/payroll/periods`). Period open/run/recalculate-dirty stay backend-only for this slice - the web page
 * only lists periods and their status; see `pages/payroll/periods.tsx`.
 */
import { apiClient } from "@senseik/api-client";

const PAYROLL_BASE = "/v1/payroll";

export type PayrollPeriodStatus = "open" | "approved" | "paid" | "locked";

export interface PayrollPeriod {
  id: string;
  year: number;
  month: number;
  status: PayrollPeriodStatus;
}

/** Tenant-wide payroll period list (`payroll.period.recalculate`, the module's only gate today - no separate view key yet). */
export async function listPayrollPeriods(): Promise<PayrollPeriod[]> {
  const { data } = await apiClient.get<PayrollPeriod[]>(`${PAYROLL_BASE}/periods`);
  return data;
}
