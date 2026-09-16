/**
 * Analytics module (roadmap Faz 7 #99): mirrors `WorkforceKpisController` (`GET /v1/analytics/kpis/workforce`).
 * Company-wide workforce KPIs computed on demand (no persisted snapshot yet). A caller whose
 * `analytics.kpi.view` data scope is narrower than "Company" (a unit manager) only gets the
 * gender-by-unit breakdown, pre-filtered server-side to their accessible units - the other KPIs are
 * tenant-wide aggregates the backend excludes rather than showing unscoped.
 */
import { apiClient } from "@senseik/api-client";

const ANALYTICS_BASE = "/v1/analytics";

export interface KpiBreakdown {
  dimension: string;
  count: number;
  share: number;
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
