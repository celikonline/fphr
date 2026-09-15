/**
 * Employees (Faz 0 items 2, 9, 10): mirrors `EmployeesController` (`api/v1/employees`).
 * Grid standard `?page&pageSize&sort&q` plus employee filters; sort fields: displayName, firstName, lastName, hireDate, status ("-field" = desc).
 */
import { apiClient } from "@senseik/api-client";
import type { BulkOperationResult, EmployeeFilter, EmployeeSelection, PagedQueryParams, PagedResult } from "@/types";

const EMPLOYEES_BASE = "/v1/employees";

export type EmployeeStatus = "Active" | "Inactive" | "OnLeave";
export const EMPLOYEE_STATUSES: EmployeeStatus[] = ["Active", "Inactive", "OnLeave"];

/** Mirrors Identity `UserAccountStatus` (camelCase enum JSON); "none" = no linked user. */
export type UserAccountStatus = "none" | "invited" | "active" | "locked" | "disabled";

export type EmployeeSortField = "displayName" | "firstName" | "lastName" | "hireDate" | "status";

export interface EmployeeListItem {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  nationalIdMasked: string | null;
  workEmail: string | null;
  personalEmail: string | null;
  workPhone: string | null;
  status: EmployeeStatus;
  orgUnitId: string | null;
  managerEmployeeId: string | null;
  managerDisplayName: string | null;
  userId: string | null;
  accountStatus: UserAccountStatus;
  hireDate: string | null;
}

export interface EmployeeDetail {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  nationalId: string | null;
  workEmail: string | null;
  personalEmail: string | null;
  workPhone: string | null;
  hireDate: string | null;
  birthDate: string | null;
  status: EmployeeStatus;
  orgUnitId: string | null;
  managerEmployeeId: string | null;
  managerDisplayName: string | null;
  userId: string | null;
  accountStatus: UserAccountStatus;
  accessType: "Employee" | "Owner" | "Admin" | null;
  lastInvitationSentAt: string | null;
}

/** Create/update body (`EmployeeRequest`); PUT replaces every field. */
export interface EmployeeInput {
  firstName: string;
  lastName: string;
  nationalId: string | null;
  workEmail: string | null;
  personalEmail: string | null;
  workPhone: string | null;
  hireDate: string | null;
  birthDate: string | null;
  orgUnitId: string | null;
  managerEmployeeId: string | null;
}

export interface DirectoryEntry {
  id: string;
  displayName: string;
  orgUnitId: string | null;
  workEmail: string | null;
  /** Null when the "work phone numbers" access setting is off for the caller. */
  workPhone: string | null;
}

export interface OrgChartNode {
  id: string;
  displayName: string;
  orgUnitId: string | null;
  children: OrgChartNode[];
}

export interface UpcomingBirthday {
  id: string;
  displayName: string;
  orgUnitId: string | null;
  nextBirthday: string;
}

export type ListEmployeesParams = PagedQueryParams & Omit<EmployeeFilter, "q">;

/** Drops empty values so the query string only carries active filters. */
function compact<T extends object>(params: T): Partial<T> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== "")
  ) as Partial<T>;
}

export async function listEmployees(params: ListEmployeesParams = {}): Promise<PagedResult<EmployeeListItem>> {
  const { includeSubUnits, ...rest } = params;
  const { data } = await apiClient.get<PagedResult<EmployeeListItem>>(EMPLOYEES_BASE, {
    params: compact({ ...rest, includeSubUnits: includeSubUnits || undefined }),
  });
  return data;
}

export async function getEmployee(id: string): Promise<EmployeeDetail> {
  const { data } = await apiClient.get<EmployeeDetail>(`${EMPLOYEES_BASE}/${id}`);
  return data;
}

export async function getMyEmployee(): Promise<EmployeeDetail> {
  const { data } = await apiClient.get<EmployeeDetail>(`${EMPLOYEES_BASE}/me`);
  return data;
}

export async function createEmployee(input: EmployeeInput): Promise<EmployeeDetail> {
  const { data } = await apiClient.post<EmployeeDetail>(EMPLOYEES_BASE, input);
  return data;
}

export async function updateEmployee(id: string, input: EmployeeInput): Promise<EmployeeDetail> {
  const { data } = await apiClient.put<EmployeeDetail>(`${EMPLOYEES_BASE}/${id}`, input);
  return data;
}

export async function bulkAssignManager(selection: EmployeeSelection, managerEmployeeId: string): Promise<BulkOperationResult> {
  const { data } = await apiClient.post<BulkOperationResult>(`${EMPLOYEES_BASE}/bulk/assign-manager`, {
    selection,
    managerEmployeeId,
  });
  return data;
}

export async function getEmployeeDirectory(
  params: PagedQueryParams & { orgUnitId?: string | null } = {}
): Promise<PagedResult<DirectoryEntry>> {
  const { data } = await apiClient.get<PagedResult<DirectoryEntry>>(`${EMPLOYEES_BASE}/directory`, { params: compact(params) });
  return data;
}

export async function getOrgChart(): Promise<OrgChartNode[]> {
  const { data } = await apiClient.get<OrgChartNode[]>(`${EMPLOYEES_BASE}/org-chart`);
  return data;
}

export async function getUpcomingBirthdays(days = 30): Promise<UpcomingBirthday[]> {
  const { data } = await apiClient.get<UpcomingBirthday[]>(`${EMPLOYEES_BASE}/upcoming-birthdays`, { params: { days } });
  return data;
}

/** Detail → full PUT body, so a section edit only overrides the fields it owns. */
export function toEmployeeInput(detail: EmployeeDetail, overrides: Partial<EmployeeInput> = {}): EmployeeInput {
  return {
    firstName: detail.firstName,
    lastName: detail.lastName,
    nationalId: detail.nationalId,
    workEmail: detail.workEmail,
    personalEmail: detail.personalEmail,
    workPhone: detail.workPhone,
    hireDate: detail.hireDate,
    birthDate: detail.birthDate,
    orgUnitId: detail.orgUnitId,
    managerEmployeeId: detail.managerEmployeeId,
    ...overrides,
  };
}

/** Minimal flat org unit (`OrgUnitDto` from `GET /v1/organization/units`) used to label/select units on employee screens. */
export interface EmployeeOrgUnitOption {
  id: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
}

export async function listOrgUnitOptions(): Promise<EmployeeOrgUnitOption[]> {
  const { data } = await apiClient.get<EmployeeOrgUnitOption[]>("/v1/organization/units");
  return data;
}
