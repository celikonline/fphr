/**
 * Genel aylık puantaj matrisi servisi (Z3.4, spec §6 B11): `GET v1/attendance/monthly-matrix?period&orgUnitId` →
 * `MonthlyMatrixDto`. Enums serialize as camelCase strings, `DateOnly` as "yyyy-MM-dd", null fields may be omitted.
 */
import { apiClient } from "@senseik/api-client";

/** Gün hücresi durum kodu. */
export type MatrixCellCode =
  | "absent"
  | "missing"
  | "overtime"
  | "full"
  | "weeklyRest"
  | "restDay"
  | "paidLeave"
  | "unpaidLeave"
  | "otherLeave"
  | "publicHoliday"
  | "incalculable"
  | "none";

/** Hücre kodlarının lejant sırası. */
export const MATRIX_CELL_CODES: readonly MatrixCellCode[] = [
  "full",
  "overtime",
  "missing",
  "absent",
  "weeklyRest",
  "restDay",
  "publicHoliday",
  "paidLeave",
  "unpaidLeave",
  "otherLeave",
  "incalculable",
  "none",
];

export interface MonthlyMatrixCell {
  /** "yyyy-MM-dd". */
  date: string;
  code: MatrixCellCode;
}

export interface MonthlyMatrixRow {
  employeeId: string;
  employeeName: string;
  cells: MonthlyMatrixCell[];
}

export interface MonthlyMatrix {
  rows: MonthlyMatrixRow[];
  isLocked: boolean;
}

/** `period` = ayın herhangi bir günü ("yyyy-MM-dd"), `orgUnitId` opsiyonel daraltma. */
export async function getMonthlyMatrix(period: string, orgUnitId?: string | null): Promise<MonthlyMatrix> {
  const { data } = await apiClient.get<MonthlyMatrix>("/v1/attendance/monthly-matrix", {
    params: { period, orgUnitId: orgUnitId || undefined },
  });
  return data;
}
