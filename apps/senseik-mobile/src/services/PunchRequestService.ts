import { apiClient } from "@/lib/apiClient";

/**
 * Eksik kayıt (punch) talebi, self-servis: mirrors `/attendance/punch-requests/me` (web
 * `attendance-punch-requests.service.ts` ile aynı uçlar). Yönetici listesi web-only.
 */
const PUNCH_REQUESTS_BASE = "/api/v1/attendance/punch-requests";

export type PunchRequestDirection = "in" | "out";
export type PunchRequestStatus = "pending" | "approved" | "rejected" | "cancelled";

export interface PunchRequest {
  id: string;
  employeeId: string;
  occurredAtUtc: string;
  direction: PunchRequestDirection;
  reason: string;
  status: PunchRequestStatus;
}

export interface CreatePunchRequestInput {
  occurredAtUtc: string;
  direction: PunchRequestDirection;
  reason: string;
}

export const PUNCH_STATUS_LABEL: Record<PunchRequestStatus, string> = {
  pending: "Bekliyor",
  approved: "Onaylandı",
  rejected: "Reddedildi",
  cancelled: "İptal edildi",
};

/** Yalnız bekleyen talep iptal edilebilir. */
export function canCancelPunchRequest(status: PunchRequestStatus): boolean {
  return status === "pending";
}

/** Yerel "yyyy-MM-dd" + "HH:mm" → Date; geçersizse null. */
export function parseLocalDateTime(date: string, time: string): Date | null {
  const dm = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date.trim());
  const tm = /^(\d{1,2}):(\d{2})$/.exec(time.trim());
  if (!dm || !tm) return null;
  const [year, month, day, hour, minute] = [dm[1], dm[2], dm[3], tm[1], tm[2]].map(Number);
  const parsed = new Date(year, month - 1, day, hour, minute);
  const valid =
    parsed.getFullYear() === year &&
    parsed.getMonth() === month - 1 &&
    parsed.getDate() === day &&
    hour < 24 &&
    minute < 60;
  return valid ? parsed : null;
}

/** Gelecekteki bir zaman için talep açılamaz. */
export function isFutureTime(value: Date, now: Date = new Date()): boolean {
  return value.getTime() > now.getTime();
}

export class PunchRequestService {
  async create(input: CreatePunchRequestInput): Promise<string> {
    const response = await apiClient.post<{ id: string }>(`${PUNCH_REQUESTS_BASE}/me`, input);
    return response.data.id;
  }

  async listMine(): Promise<PunchRequest[]> {
    const response = await apiClient.get<PunchRequest[]>(`${PUNCH_REQUESTS_BASE}/me`);
    return response.data;
  }

  async cancel(id: string): Promise<void> {
    await apiClient.post(`${PUNCH_REQUESTS_BASE}/${id}/cancel`);
  }
}

export const punchRequestService = new PunchRequestService();
