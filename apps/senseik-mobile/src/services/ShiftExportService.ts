import { File, Paths } from "expo-file-system";
import * as XLSX from "xlsx";
import type { ShiftAssignment } from "./ShiftService";

export type ShiftExportHeaders = {
  date: string;
  start: string;
  end: string;
  breakMinutes: string;
  night: string;
  note: string;
  sheetName: string;
  yes: string;
  no: string;
};

/** Creates an actual .xlsx workbook for the employee's published shifts in the selected month. */
export function createShiftExport(
  shifts: ShiftAssignment[],
  year: number,
  month0: number,
  headers: ShiftExportHeaders
): string {
  const sorted = [...shifts].sort(
    (a, b) => a.date.localeCompare(b.date) || a.start.localeCompare(b.start)
  );
  const rows = sorted.map((shift) => [
    shift.date,
    shift.start.slice(0, 5),
    shift.end.slice(0, 5),
    shift.breakMinutes,
    shift.isNight ? headers.yes : headers.no,
    shift.note ?? "",
  ]);
  const worksheet = XLSX.utils.aoa_to_sheet([
    [headers.date, headers.start, headers.end, headers.breakMinutes, headers.night, headers.note],
    ...rows,
  ]);
  worksheet["!cols"] = [
    { wch: 14 },
    { wch: 10 },
    { wch: 10 },
    { wch: 16 },
    { wch: 16 },
    { wch: 36 },
  ];
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, headers.sheetName);

  const fileName = `vardiyam-${year}-${String(month0 + 1).padStart(2, "0")}.xlsx`;
  const file = new File(Paths.cache, fileName);
  if (file.exists) file.delete();
  file.create();
  const bytes = XLSX.write(workbook, { bookType: "xlsx", type: "array" }) as ArrayBuffer;
  try {
    file.write(new Uint8Array(bytes));
  } catch (error) {
    if (file.exists) file.delete();
    throw error;
  }
  return file.uri;
}

/** Removes a generated personal schedule from cache after the share sheet hands it off. */
export function deleteCachedShiftExport(fileUri: string): void {
  try {
    const file = new File(fileUri);
    if (file.exists) file.delete();
  } catch {
    // Cache is reclaimed by the OS if best-effort cleanup fails.
  }
}
