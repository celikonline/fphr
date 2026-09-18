/**
 * Analytics module (roadmap Faz 7 #99): mirrors `WorkforceKpisController` (`GET /v1/analytics/kpis/workforce`).
 * Company-wide workforce KPIs computed on demand (no persisted snapshot yet). A caller whose
 * `analytics.kpi.view` data scope is narrower than "Company" (a unit manager) only gets the
 * gender-by-unit breakdown, pre-filtered server-side to their accessible units - the other KPIs are
 * tenant-wide aggregates the backend excludes rather than showing unscoped.
 */
import { apiClient } from "@senseik/api-client";

const ANALYTICS_BASE = "/v1/analytics";

/**
 * One breakdown row. `count`/`share` are omitted by the API (`WhenWritingNull`) exactly when `isSuppressed` is
 * true (G-10-21, 11.3.12 "Küçük hücre gizleme"): a non-zero cell under the backend's minimum cell size (5) never
 * carries its exact number - `dimension` still names the category, callers must show a "gizli" state instead of
 * the value.
 */
export interface KpiBreakdown {
  dimension: string;
  count?: number;
  share?: number;
  isSuppressed: boolean;
}

export interface KpiResult {
  key: string;
  label: string;
  value: number;
  unit: string;
  periodStart: string;
  periodEnd: string;
  breakdown: KpiBreakdown[];
}

export interface WorkforceKpiParams {
  from?: string;
  to?: string;
}

export async function getWorkforceKpis(params: WorkforceKpiParams = {}): Promise<KpiResult[]> {
  const { data } = await apiClient.get<KpiResult[]>(`${ANALYTICS_BASE}/kpis/workforce`, { params });
  return data;
}

/** `byLeaveType` breakdown row (S-15 sözleşmesi, faz-10 BE). */
export interface LeaveUsageByType {
  leaveTypeName: string;
  usedDays: number;
}

/** `GET /v1/analytics/kpis/leave-usage` (S-15): year is mandatory, `orgUnitId` scopes to a unit manager's own unit. */
export interface LeaveUsageKpi {
  totalEntitledDays: number;
  usedDays: number;
  usageRate: number;
  byLeaveType: LeaveUsageByType[];
}

export interface LeaveUsageKpiParams {
  year: number;
  orgUnitId?: string;
}

/** Not deployed in every environment yet (faz-10 R0) - callers should hide the widget on a 404. */
export async function getLeaveUsageKpi(params: LeaveUsageKpiParams): Promise<LeaveUsageKpi> {
  const { data } = await apiClient.get<LeaveUsageKpi>(`${ANALYTICS_BASE}/kpis/leave-usage`, { params });
  return data;
}

/** One currency's total (S-17): no cross-currency conversion, each currency is summed separately. */
export interface OvertimeCostTotal {
  currency: string;
  amount: number;
}

/**
 * `GET /v1/analytics/kpis/overtime-cost` (S-17): hourly-rate x overtime hours x 1.5 (4857 s. İş K.
 * md.41/63), grouped per salary currency. Requires `analytics.kpi.view` **and** the salary-view
 * permission (wage-sensitive) - callers without the latter get 403, same as a 404 (uç yayılı değil)
 * should hide the widget rather than show an error.
 */
export interface OvertimeCostKpi {
  year: number;
  month: number;
  totals: OvertimeCostTotal[];
  employeeCount: number;
  excludedEmployeeCount: number;
}

export interface OvertimeCostKpiParams {
  year: number;
  month: number;
  orgUnitId?: string;
}

export async function getOvertimeCostKpi(params: OvertimeCostKpiParams): Promise<OvertimeCostKpi> {
  const { data } = await apiClient.get<OvertimeCostKpi>(`${ANALYTICS_BASE}/kpis/overtime-cost`, { params });
  return data;
}
