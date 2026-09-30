/**
 * Kayıt (punch) düzeltme talepleri: `AttendancePunch` onay tipi (attendance.punchrequest.manage,
 * attendance.my.punchrequest). Uçlar: POST/GET `me`, POST `{id}/cancel`, yönetici GET listesi.
 */
import { apiClient } from "@senseik/api-client";
import type { PunchDirection } from "./attendance-daily.service";

const BASE = "/v1/attendance/punch-requests";

export type PunchRequestStatus = "pending" | "approved" | "rejected" | "cancelled";

export interface PunchRequest {
  id: string;
  employeeId: string;
  /** Sunucu her zaman doldurur; eski veride boş olabilir. */
  employeeName?: string | null;
  workDate: string;
  occurredAtUtc: string;
  direction: PunchDirection;
  reason: string;
  status: PunchRequestStatus;
  decidedAtUtc?: string | null;
  approvalRequestId?: string | null;
}

export interface CreatePunchRequestInput {
  occurredAtUtc: string;
  direction: PunchDirection;
  reason: string;
}

export async function createPunchRequest(dto: CreatePunchRequestInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${BASE}/me`, dto);
  return data.id;
}

export async function listMyPunchRequests(): Promise<PunchRequest[]> {
  const { data } = await apiClient.get<PunchRequest[]>(`${BASE}/me`);
  return data;
}

export async function cancelPunchRequest(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/cancel`);
}

/** Yönetici görünümü (attendance.punchrequest.manage). */
export async function listPunchRequests(params?: { status?: PunchRequestStatus }): Promise<PunchRequest[]> {
  const { data } = await apiClient.get<PunchRequest[]>(BASE, { params });
  return data;
}
