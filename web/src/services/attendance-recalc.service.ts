/**
 * Toplu yeniden hesaplama (spec §7): mirrors `IAttendanceDayRecalculator` / `RecalculationStatusDto`
 * (attendance.recalculate.run). Dates are `DateOnly` ("yyyy-MM-dd").
 */
import { apiClient } from "@senseik/api-client";

const RECALC_BASE = "/v1/attendance/recalculations";

/** `RecalculationStatusDto`. */
export interface RecalculationStatus {
  jobId: string;
  state: string;
  total: number;
  done: number;
  error?: string | null;
}

export interface RecalculationRequest {
  from: string;
  to: string;
  /** Omitted = all employees. */
  employeeIds?: string[];
}

/** Queues a background recalculation; returns the job id to poll. */
export async function enqueueRecalculation(request: RecalculationRequest): Promise<string> {
  const { data } = await apiClient.post<{ jobId: string }>(RECALC_BASE, request);
  return data.jobId;
}

export async function getRecalculationStatus(jobId: string): Promise<RecalculationStatus> {
  const { data } = await apiClient.get<RecalculationStatus>(`${RECALC_BASE}/${jobId}`);
  return data;
}
