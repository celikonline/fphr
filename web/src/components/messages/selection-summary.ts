import type { EmployeeSelection } from "@/types";

/** True when the selection names no employees and has no filter (server rejects it as "recipients required"). */
export function isSelectionEmpty(selection: EmployeeSelection | null | undefined): boolean {
  if (!selection) return true;
  return (selection.employeeIds?.length ?? 0) === 0 && !selection.filter;
}
