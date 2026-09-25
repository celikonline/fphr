/** Vardiya rotasyonları (attendance.rotation.view/manage). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/rotations";

export interface Rotation {
  id: string;
  name: string;
  /** Shift definition ids in cycle order. */
  shiftDefinitionIds: string[];
}

export async function listRotations(): Promise<Rotation[]> {
  const { data } = await apiClient.get<Rotation[]>(BASE);
  return data;
}
