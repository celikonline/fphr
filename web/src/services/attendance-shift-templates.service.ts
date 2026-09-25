/**
 * Vardiya planı şablonları (Z3.2, B10): `ShiftTemplatesController` — seçimden şablon kaydet, listele, tarih aralığına uygula.
 * Tarihler `DateOnly` (yyyy-MM-dd). Excel içe aktarma DataTransfer `ShiftPlanImportTemplate` şablonu üzerinden yürür.
 */
import { apiClient } from "@senseik/api-client";
import type { ShiftDayType } from "./attendance-shift-calendar.service";

const BASE = "/v1/attendance/shift-templates";

/** DataTransfer içe aktarma şablon kodu (B10 `ShiftPlanImportTemplate`). */
export const SHIFT_PLAN_IMPORT_TEMPLATE_CODE = "shift-plan";

export interface ShiftTemplateEntry {
  employeeId: string;
  /** Şablon başlangıç gününe göre 0 tabanlı gün ofseti. */
  dayOffset: number;
  shiftDefinitionId?: string | null;
  dayType: ShiftDayType;
}

export interface ShiftTemplate {
  id: string;
  name: string;
  periodLengthDays: number;
  entries: ShiftTemplateEntry[];
  createdAt?: string;
}

export interface CreateShiftTemplateInput {
  name: string;
  periodLengthDays: number;
  entries: ShiftTemplateEntry[];
}

export interface ApplyShiftTemplateInput {
  /** Şablonun 0. gününün düşeceği tarih. */
  startDate: string;
  /** Uygulama tekrar sayısı (dönem uzunluğu kadar bloklar); varsayılan 1. */
  repeatCount?: number;
  overwritePublished?: boolean;
}

export interface ApplyShiftTemplateResult {
  affectedCount: number;
  skipped: { employeeId: string; date: string; reason: string }[];
}

export async function listShiftTemplates(): Promise<ShiftTemplate[]> {
  const { data } = await apiClient.get<ShiftTemplate[]>(BASE);
  return data;
}

export async function createShiftTemplate(input: CreateShiftTemplateInput): Promise<ShiftTemplate> {
  const { data } = await apiClient.post<ShiftTemplate>(BASE, input);
  return data;
}

export async function applyShiftTemplate(id: string, input: ApplyShiftTemplateInput): Promise<ApplyShiftTemplateResult> {
  const { data } = await apiClient.post<ApplyShiftTemplateResult>(`${BASE}/${id}/apply`, input);
  return data;
}
