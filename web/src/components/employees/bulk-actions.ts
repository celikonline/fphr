import { bulkInviteEmployees } from "@/services/invitations.service";
import { bulkAssignManager } from "@/services/employees.service";
import type { BulkOperationResult, EmployeeSelection } from "@/types";

export interface BulkActionInput {
  managerEmployeeId?: string;
}

/** One entry of the employee grid's "Toplu işlemler → İşlem seç" menu (roadmap #9). */
export interface BulkActionDefinition {
  code: string;
  /** Key in the "employees" namespace. */
  labelKey: string;
  permission: string;
  /** Roadmap phase when the action ships later; rendered disabled with a "Faz N" badge. */
  phase?: number;
  /** Extra input collected in a dialog before running; otherwise a plain confirmation is shown. */
  input?: "manager";
  run?: (selection: EmployeeSelection, input: BulkActionInput) => Promise<BulkOperationResult>;
}

export const BULK_ACTIONS: BulkActionDefinition[] = [
  {
    code: "sendLoginInfo",
    labelKey: "bulk.actions.sendLoginInfo",
    permission: "identity.users.invite",
    run: (selection) => bulkInviteEmployees(selection),
  },
  {
    code: "assignManager",
    labelKey: "bulk.actions.assignManager",
    permission: "employee.employees.bulk_update",
    input: "manager",
    run: (selection, input) => bulkAssignManager(selection, input.managerEmployeeId!),
  },
  {
    code: "addWorkSchedule",
    labelKey: "bulk.actions.addWorkSchedule",
    permission: "employee.employees.bulk_update",
    phase: 1,
  },
  {
    code: "assignLeaveRule",
    labelKey: "bulk.actions.assignLeaveRule",
    permission: "employee.employees.bulk_update",
    phase: 1,
  },
];

/** i18n key for a backend item/error code (dots are i18next key separators, so they become underscores). */
export function bulkCodeKey(code: string): string {
  return `employees:codes.${code.replace(/\./g, "_")}`;
}
