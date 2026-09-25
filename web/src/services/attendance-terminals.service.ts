/** PDKS terminalleri (attendance.terminal.view/manage). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/terminals";

export interface Terminal {
  id: string;
  name: string;
  locationId?: string | null;
  isActive: boolean;
}

export async function listTerminals(): Promise<Terminal[]> {
  const { data } = await apiClient.get<Terminal[]>(BASE);
  return data;
}
