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
