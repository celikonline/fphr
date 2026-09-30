import { apiClient } from "@senseik/api-client";
import type { PunchSource } from "@/services/attendance-daily.service";

const BOARD_BASE = "/v1/attendance/board";

/** `LiveTerminalStatusDto` (Z5.3): terminal canlı durumu. Tarihler UTC ISO. */
export interface LiveTerminalStatus {
  terminalId: string;
  code: string;
  name: string;
  groupName?: string | null;
  isActive: boolean;
  online: boolean;
  lastSeenAtUtc?: string | null;
  lastPunchAtUtc?: string | null;
  todayPunchCount: number;
}

/** attendance.terminal.view; terminal listesi + çevrimiçi durumu. */
export async function getLiveTerminals(): Promise<LiveTerminalStatus[]> {
  const { data } = await apiClient.get<LiveTerminalStatus[]>(`${BOARD_BASE}/terminals`);
  return data;
}

/** Canlı pano satırındaki son okutma alanları (Z5.3); eski yanıtlarda yoktur. */
export interface LiveBoardLastPunch {
  lastPunchSource?: PunchSource | null;
  lastPunchTerminalId?: string | null;
  lastPunchTerminalName?: string | null;
}
