/**
 * Vardiya tanımları (attendance.shiftdefinition.view/manage). Controller: v1/attendance/shift-definitions.
 * Alan adları spec §4 `ShiftDefinition` ile birebir; enum'lar camelCase string, null alanlar yanıtta yoktur.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/shift-definitions";

export type ShiftType = "fixed" | "flexible" | "dynamic";

export interface ShiftBreak {
  /** Gün başından dakika; yoksa mola serbest yerleşir. */
  startMinute?: number | null;
  durationMinutes: number;
  isFlexible: boolean;
}

export interface ShiftDefinition {
  id?: string;
  code: string;
  name: string;
  colorHex: string;
  type: ShiftType;
  /** Gün başından dakika (0-1439). */
  startMinute: number;
  /** endMinute <= startMinute ise vardiya gece yarısını aşar. */
  endMinute: number;
  startsPreviousDay: boolean;
  /** Yalnız Flexible. */
  durationMinutes?: number | null;
  flexStartEarliestMinute?: number | null;
  flexStartLatestMinute?: number | null;
  breaks: ShiftBreak[];
  lateToleranceMinutes: number;
  earlyLeaveToleranceMinutes: number;
  earlyArrivalOvertimeMinMinutes?: number | null;
  earlyArrivalOvertimeMaxMinutes?: number | null;
  lateDepartureOvertimeMinMinutes?: number | null;
  lateDepartureOvertimeMaxMinutes?: number | null;
  bonusMinutes: number;
  isActive: boolean;
}

export interface SaveShiftDefinitionOptions {
  /** Daha önce planlanmış geçmiş günleri yeni tanımla yeniden hesaplat. */
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
