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
  /** Yalnız okunur; değiştirmek için setShiftDefinitionActive (kayıt gövdesinde gönderilmez). */
  isActive: boolean;
}

/** Kayıt gövdesi: sunucu IsActive/Id kabul etmez. */
export type ShiftDefinitionInput = Omit<ShiftDefinition, "id" | "isActive">;

export interface SaveShiftDefinitionOptions {
  /** Daha önce planlanmış geçmiş günleri yeni tanımla yeniden hesaplat. */
  applyToPast: boolean;
}

/** PUT yanıtı (ShiftDefinitionSaveResultDto); POST için affectedAssignmentCount 0 olarak doldurulur. */
export interface ShiftDefinitionSaveResult {
  id: string;
  recalculationJobId?: string | null;
  affectedAssignmentCount: number;
}

/** Varsayılan yalnız aktifler (palet); yönetim sayfası includeInactive ister. */
export async function listShiftDefinitions(options?: {
  includeInactive?: boolean;
}): Promise<ShiftDefinition[]> {
  const { data } = await apiClient.get<ShiftDefinition[]>(BASE, {
    params: options?.includeInactive ? { includeInactive: true } : undefined,
  });
  return data;
}

export async function saveShiftDefinition(
  dto: ShiftDefinition,
  { applyToPast }: SaveShiftDefinitionOptions
): Promise<ShiftDefinitionSaveResult> {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { id, isActive, ...body } = dto;
  if (id) {
    const { data } = await apiClient.put<ShiftDefinitionSaveResult>(`${BASE}/${id}`, body, {
      params: { applyToPast },
    });
    return data;
  }
  const { data } = await apiClient.post<string>(BASE, body);
  return { id: data, affectedAssignmentCount: 0 };
}

export async function setShiftDefinitionActive(id: string, isActive: boolean): Promise<void> {
  await apiClient.put(`${BASE}/${id}/active`, { isActive });
}
