/**
 * Kayıt (punch) düzeltme talepleri: `AttendancePunch` onay tipi (attendance.punchrequest.manage,
 * attendance.my.punchrequest). Skeleton DTOs, refined by the lane that builds the pages.
 */
import { apiClient } from "@senseik/api-client";
import type { PunchDirection } from "./attendance-daily.service";

const BASE = "/v1/attendance/punch-requests";

export interface PunchRequest {
  id: string;
  employeeId: string;
  occurredAtUtc: string;
  direction: PunchDirection;
  reason: string;
  status: string;
}

export interface CreatePunchRequestInput {
  occurredAtUtc: string;
  direction: PunchDirection;
  reason: string;
}

export async function createPunchRequest(dto: CreatePunchRequestInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(BASE, dto);
  return data.id;
}

export async function listMyPunchRequests(): Promise<PunchRequest[]> {
  const { data } = await apiClient.get<PunchRequest[]>(`${BASE}/mine`);
  return data;
}
