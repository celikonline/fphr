/**
 * Employees (Faz 0 items 2, 9, 10): mirrors `EmployeesController` (`api/v1/employees`).
 * Grid standard `?page&pageSize&sort&q` plus employee filters; sort fields: displayName, firstName, lastName, hireDate, status ("-field" = desc).
 */
import { apiClient } from "@senseik/api-client";
import type { BulkOperationResult, EmployeeFilter, EmployeeSelection, PagedQueryParams, PagedResult } from "@/types";

const EMPLOYEES_BASE = "/v1/employees";

/** Draft = quick-add without a hire date; Active = hired; Suspended/Terminated are later lifecycle transitions. */
export type EmployeeStatus = "Draft" | "Active" | "Suspended" | "Terminated";
export const EMPLOYEE_STATUSES: EmployeeStatus[] = ["Draft", "Active", "Suspended", "Terminated"];

/** Mirrors Identity `UserAccountStatus` (camelCase enum JSON); "none" = no linked user. */
export type UserAccountStatus = "none" | "invited" | "active" | "locked" | "disabled";

export type EmployeeSortField = "displayName" | "firstName" | "lastName" | "hireDate" | "status";

/** Native C# enums (unlike Status/SuspensionReason) go through the API's camelCase JsonStringEnumConverter. */
export type Gender = "unspecified" | "male" | "female" | "other";
export type MaritalStatus = "unspecified" | "single" | "married" | "divorced";
export type EducationStatus = "student" | "graduate";
export type HighestEducationLevel = "primary" | "middleSchool" | "highSchool" | "associate" | "bachelor" | "master" | "doctorate";
export type MilitaryStatus = "notCompleted" | "completed" | "deferred" | "exempt" | "draftEvader" | "absentee";
export type DisabilityDegree = "none" | "first" | "second" | "third";
export type BloodType = "oPos" | "oNeg" | "aPos" | "aNeg" | "bPos" | "bNeg" | "abPos" | "abNeg";
export type ContractType = "indefinite" | "fixedTerm";
export type EmploymentType =
  | "fullTime"
  | "partTime"
  | "temporary"
  | "contract"
  | "freelance"
  | "intern"
  | "mobile"
  | "apprentice"
  | "seasonal"
  | "agencyWorker"
  | "subcontractor";
/** Employee.SuspensionReason.ToString() (like Status, pre-stringified server-side: exact PascalCase member name). */
export type SuspensionReason = "MilitaryService" | "UnionOffice" | "LongUnpaidLeave" | "Detention" | "HealthReport" | "ForceMajeure" | "Other";

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

/** Sensitive fields (nationalId when unmasked, militaryStatus, disabilityDegree, bloodType) are null/masked without employee.sensitive.view. */
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
  gender: Gender;
  maritalStatus: MaritalStatus;
  childCount: number | null;
  educationStatus: EducationStatus | null;
  highestEducationLevel: HighestEducationLevel | null;
  militaryStatus: MilitaryStatus | null;
  disabilityDegree: DisabilityDegree | null;
  bloodType: BloodType | null;
  mobilePhone: string | null;
  homePhone: string | null;
  addressLine: string | null;
  city: string | null;
  postalCode: string | null;
  contractType: ContractType | null;
  employmentType: EmploymentType | null;
  suspensionReason: SuspensionReason | null;
  /** "İzne esas hak ediş tarihi" (defaults to hireDate; may be earlier). */
  leaveAccrualStartDate: string | null;
  /** Derived: validFrom of the career record in effect today. */
  positionStartDate: string | null;
}

/**
 * Create/update body (`EmployeeRequest`); PUT replaces every field. The fields below `managerEmployeeId` are all
 * optional client-side (the quick-add dialog only sets first/last name) - the API defaults them the same way
 * (Gender/MaritalStatus to "unspecified", the rest to null).
 */
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
  gender?: Gender;
  maritalStatus?: MaritalStatus;
  childCount?: number | null;
  educationStatus?: EducationStatus | null;
  highestEducationLevel?: HighestEducationLevel | null;
  militaryStatus?: MilitaryStatus | null;
  disabilityDegree?: DisabilityDegree | null;
  bloodType?: BloodType | null;
  mobilePhone?: string | null;
  homePhone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  postalCode?: string | null;
  contractType?: ContractType | null;
  employmentType?: EmploymentType | null;
  /** Omitted/null keeps the stored value (or defaults it to hireDate the first time one is set). */
  leaveAccrualStartDate?: string | null;
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

/** Mirrors `DepartmentDistributionItemDto`: active headcount per Department-level org unit ("Çalışan Dağılımı" widget). */
export interface DepartmentDistributionItem {
  orgUnitId: string | null;
  departmentName: string | null;
  count: number;
}

export async function getDepartmentDistribution(): Promise<DepartmentDistributionItem[]> {
  const { data } = await apiClient.get<DepartmentDistributionItem[]>(`${EMPLOYEES_BASE}/department-distribution`);
  return data;
}

/**
 * Detail → full PUT body, so a section edit only overrides the fields it owns. Sensitive fields (nationalId,
 * militaryStatus, disabilityDegree, bloodType) round-trip whatever the detail held (masked/null without
 * employee.sensitive.view) - the server always preserves the real stored values for a caller who cannot see them.
 */
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
    gender: detail.gender,
    maritalStatus: detail.maritalStatus,
    childCount: detail.childCount,
    educationStatus: detail.educationStatus,
    highestEducationLevel: detail.highestEducationLevel,
    militaryStatus: detail.militaryStatus,
    disabilityDegree: detail.disabilityDegree,
    bloodType: detail.bloodType,
    mobilePhone: detail.mobilePhone,
    homePhone: detail.homePhone,
    addressLine: detail.addressLine,
    city: detail.city,
    postalCode: detail.postalCode,
    contractType: detail.contractType,
    employmentType: detail.employmentType,
    leaveAccrualStartDate: detail.leaveAccrualStartDate ?? null,
    ...overrides,
  };
}

/** Minimal flat org unit (`OrgUnitDto` from `GET /v1/organization/units`) used to label/select units on employee screens. */
export interface EmployeeOrgUnitOption {
  id: string;
  typeId: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
}

export async function listOrgUnitOptions(): Promise<EmployeeOrgUnitOption[]> {
  const { data } = await apiClient.get<EmployeeOrgUnitOption[]>("/v1/organization/units");
  return data;
}

/** Active hierarchy level (`OrgUnitTypeDto` from `GET /v1/organization/unit-types?activeOnly=true`). */
export interface EmployeeOrgLevelOption {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
}

export async function listOrgLevelOptions(): Promise<EmployeeOrgLevelOption[]> {
  const { data } = await apiClient.get<EmployeeOrgLevelOption[]>("/v1/organization/unit-types", { params: { activeOnly: true } });
  return data;
}

// ---------------------------------------------------------------- Career history (Kariyer)

/** Mirrors `CareerChangeType` (camelCase enum JSON). */
export type CareerChangeType =
  | "hire"
  | "promotion"
  | "lateralMove"
  | "demotion"
  | "titleChange"
  | "managerChange"
  | "groupTransfer"
  | "businessTransfer"
  | "rehire"
  | "other";

export interface OrgAssignment {
  orgUnitTypeId: string;
  orgUnitId: string;
}

/** `CareerRecordDto`; the API returns the timeline newest first. */
export interface CareerRecord {
  id: string;
  validFrom: string;
  validTo: string | null;
  changeType: CareerChangeType;
  employmentType: EmploymentType | null;
  managerEmployeeId: string | null;
  managerDisplayName: string | null;
  note: string | null;
  assignments: OrgAssignment[];
  isCurrent: boolean;
}

/** `CareerRecordRequest` body ("Pozisyon ekle" / edit). */
export interface CareerRecordInput {
  validFrom: string;
  changeType: CareerChangeType;
  employmentType: EmploymentType | null;
  managerEmployeeId: string | null;
  note: string | null;
  assignments: OrgAssignment[];
}

export async function getEmployeeCareer(employeeId: string): Promise<CareerRecord[]> {
  const { data } = await apiClient.get<CareerRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/career`);
  return data;
}

export async function getMyCareer(): Promise<CareerRecord[]> {
  const { data } = await apiClient.get<CareerRecord[]>(`${EMPLOYEES_BASE}/me/career`);
  return data;
}

export async function addCareerRecord(employeeId: string, input: CareerRecordInput): Promise<CareerRecord[]> {
  const { data } = await apiClient.post<CareerRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/career`, input);
  return data;
}

export async function updateCareerRecord(employeeId: string, recordId: string, input: CareerRecordInput): Promise<CareerRecord[]> {
  const { data } = await apiClient.put<CareerRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/career/${recordId}`, input);
  return data;
}

export async function deleteCareerRecord(employeeId: string, recordId: string): Promise<CareerRecord[]> {
  const { data } = await apiClient.delete<CareerRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/career/${recordId}`);
  return data;
}
