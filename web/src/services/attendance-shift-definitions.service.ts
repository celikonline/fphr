/**
 * Vardiya tanımları (attendance.shiftdefinition.view/manage). Skeleton: the lane that builds the page refines the DTO
 * against the real controller; the function names are the contract consumed by the F lanes.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/shift-definitions";

export interface ShiftDefinition {
  id?: string;
  name: string;
  /** "HH:mm:ss". */
  startTime: string;
  endTime: string;
  breakMinutes: number;
  isActive?: boolean;
}

export interface SaveShiftDefinitionOptions {
  /** Re-plan already-planned past days with the new definition. */
  applyToPast: boolean;
}

export async function listShiftDefinitions(): Promise<ShiftDefinition[]> {
  const { data } = await apiClient.get<ShiftDefinition[]>(BASE);
  return data;
}

export async function saveShiftDefinition(
  dto: ShiftDefinition,
  { applyToPast }: SaveShiftDefinitionOptions,
): Promise<ShiftDefinition> {
  const params = { applyToPast };
  const { data } = dto.id
    ? await apiClient.put<ShiftDefinition>(`${BASE}/${dto.id}`, dto, { params })
    : await apiClient.post<ShiftDefinition>(BASE, dto, { params });
  return data;
}
