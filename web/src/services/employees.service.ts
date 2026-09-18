/**
 * Employees (Faz 0 items 2, 9, 10): mirrors `EmployeesController` (`api/v1/employees`).
 * Grid standard `?page&pageSize&sort&q` plus employee filters; sort fields: displayName, firstName, lastName, hireDate, status ("-field" = desc).
 */
import { apiClient } from "@senseik/api-client";
import type { BulkOperationResult, EmployeeFilter, EmployeeSelection, PagedQueryParams, PagedResult } from "@/types";
import type { CustomFieldDataType, CustomFieldSection } from "@/services/custom-fields.service";

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
  contractType: ContractType | null;
  employmentType: EmploymentType | null;
  /** "Eksik bilgi" badge: a hired record with an unfilled personnel field (drafts are never flagged). */
  hasMissingInfo: boolean;
  /** "Title" level unit of the placement chain (card view); missing when unplaced. */
  titleName?: string | null;
}

/**
 * A custom field's definition joined with this employee's stored value ("Özel alanlar", E4). `value` is null when
 * unset, or when it is sensitive and the caller lacks employee.sensitive.view (the definition itself is still shown).
 * A definition the caller cannot see at all (AccountShowCustomFields off, non-owner/non-updater) is omitted entirely.
 */
export interface CustomFieldValue {
  definitionId: string;
  key: string;
  name: string;
  dataType: CustomFieldDataType;
  section: CustomFieldSection;
  options: string[];
  isRequired: boolean;
  isEmployeeEditable: boolean;
  isSensitive: boolean;
  value: string | null;
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
  /** Resolved from the employee's org placement; missing when not assigned. */
  departmentName?: string | null;
  titleName?: string | null;
  /** Custom field values visible to the caller (see `CustomFieldValue`); empty when none are defined or none are visible. */
  customFields: CustomFieldValue[] | null;
  /** Subcontractor the employee belongs to ("alt işveren çalışanı"); null for the tenant's own staff. */
  subcontractorId?: string | null;
  subcontractorName?: string | null;
  /** Set once the employee is Terminated ("İşten ayrılış sihirbazı"). */
  terminationDate?: string | null;
  terminationReasonId?: string | null;
  terminationType?: TerminationType | null;
  sgkExitCode?: string | null;
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
  /** "Title"/"Company" level units of the placement chain; resolved server-side, missing when unplaced. */
  titleName?: string | null;
  companyId?: string | null;
  companyName?: string | null;
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

// ---------------------------------------------------------------- Custom field values (Özel alanlar, E4)

/**
 * HR/admin write path: only the entries present in `values` change (definitionId -> raw value; null clears it). Any
 * active definition may be set - a sensitive one without employee.sensitive.view is silently preserved server-side.
 */
export async function setEmployeeCustomFields(employeeId: string, values: Record<string, string | null>): Promise<CustomFieldValue[]> {
  const { data } = await apiClient.put<CustomFieldValue[]>(`${EMPLOYEES_BASE}/${employeeId}/custom-fields`, { values });
  return data;
}

/** Self-service profile edit body ("Self-servis düzenleme"); `customFieldValues` may only touch IsEmployeeEditable definitions. */
export interface UpdateMyProfileInput {
  personalEmail: string | null;
  mobilePhone: string | null;
  homePhone: string | null;
  addressLine: string | null;
  city: string | null;
  postalCode: string | null;
  customFieldValues: Record<string, string | null> | null;
}

export async function updateMyProfile(input: UpdateMyProfileInput): Promise<EmployeeDetail> {
  const { data } = await apiClient.put<EmployeeDetail>(`${EMPLOYEES_BASE}/me/profile`, input);
  return data;
}

// ---------------------------------------------------------------- Lifecycle: suspend / terminate / rehire (E5a)

/** 4857 termination types; camelCase enum JSON like the other native C# enums. */
export type TerminationType =
  | "validCause_Art18"
  | "employeeJustCause_Art24"
  | "employerJustCause_Art25"
  | "mutualRescission"
  | "resignation"
  | "fixedTermExpiry"
  | "other";

export const TERMINATION_TYPES: TerminationType[] = [
  "validCause_Art18",
  "employeeJustCause_Art24",
  "employerJustCause_Art25",
  "mutualRescission",
  "resignation",
  "fixedTermExpiry",
  "other",
];

export const SUSPENSION_REASONS: SuspensionReason[] = [
  "MilitaryService",
  "UnionOffice",
  "LongUnpaidLeave",
  "Detention",
  "HealthReport",
  "ForceMajeure",
  "Other",
];

/** A row of the tenant's termination reason catalog; system rows come from the seed, tenant rows are added in Settings. */
export interface TerminationReason {
  id: string;
  code: string;
  name: string;
  defaultSgkExitCode: string | null;
  defaultTerminationType: TerminationType | null;
  isSystem: boolean;
  isActive: boolean;
  /** SGK codes valid for defaultTerminationType (senseik-006/F-01); null when the type is unset or unrestricted (Other). */
  allowedSgkExitCodes: string[] | null;
}

export interface TerminationReasonInput {
  /** Only read on create; the code is immutable afterwards. */
  code?: string | null;
  name: string;
  defaultSgkExitCode: string | null;
  defaultTerminationType: TerminationType | null;
  sortOrder: number;
}

export interface TerminateEmployeeInput {
  terminationDate: string;
  terminationReasonId: string;
  /** Null = use the reason's default. */
  terminationType: TerminationType | null;
  sgkExitCode: string | null;
  /** Exit settlement: they close the employment period and decide whether it counts towards a later rehire. */
  severancePaid: boolean;
  leaveSettledAtExit: boolean;
}

/** Advisory collective-dismissal status of the rolling 30-day window (4857 md.29); never blocks a termination. */
export interface CollectiveDismissalStatus {
  inScope: boolean;
  showWarning: boolean;
  windowStart: string;
  windowEnd: string;
  dismissalsInWindow: number;
  threshold: number;
  headcount: number;
  noticeDays: number;
  rehirePriorityMonths: number;
  tempWorkerBanMonths: number;
  warningMessage: string | null;
}

export interface TerminateEmployeeResult {
  employee: EmployeeDetail;
  jobSecurityInScope: boolean;
  jobSecurityWarning: boolean;
  jobSecurityWarningMessage: string | null;
  collectiveDismissal: CollectiveDismissalStatus | null;
  /** Count of the employee's still-assigned assets (zimmet) at exit time; advisory only, never blocks the wizard. */
  openAssetCount: number;
}

export type EmploymentPeriodSource = "hire" | "rehire" | "groupTransfer" | "businessTransfer" | "import";

export interface EmploymentPeriod {
  id: string;
  startDate: string;
  endDate: string | null;
  source: EmploymentPeriodSource;
  terminationReasonId: string | null;
  terminationReasonName: string | null;
  severancePaid: boolean;
  leaveSettledAtExit: boolean;
  countedDays: number;
}

/** Merged seniority; the start dates are "as if worked continuously" dates (gaps and uncounted periods deducted). */
export interface EmploymentSeniority {
  asOf: string;
  leaveSeniorityStartDate: string | null;
  leaveSeniorityDays: number;
  severanceSeniorityStartDate: string | null;
  severanceSeniorityDays: number;
  jobSecuritySeniorityMonths: number;
}

export interface EmploymentHistory {
  periods: EmploymentPeriod[];
  seniority: EmploymentSeniority;
}

export interface RehireEmployeeInput {
  hireDate: string;
  /** "Önceki dönem izin kıdemine sayılsın mı": carries the counted previous periods into the leave accrual start. */
  mergePreviousPeriods: boolean;
  /** Overrides the merged suggestion outright. */
  leaveAccrualStartDate: string | null;
}

export interface RehireEmployeeResult {
  employee: EmployeeDetail;
  employmentHistory: EmploymentHistory;
}

export async function suspendEmployee(employeeId: string, reason: SuspensionReason): Promise<EmployeeDetail> {
  const { data } = await apiClient.post<EmployeeDetail>(`${EMPLOYEES_BASE}/${employeeId}/suspend`, { reason });
  return data;
}

export async function reactivateEmployee(employeeId: string): Promise<EmployeeDetail> {
  const { data } = await apiClient.post<EmployeeDetail>(`${EMPLOYEES_BASE}/${employeeId}/reactivate`);
  return data;
}

export async function getTerminationReasons(includeInactive = false): Promise<TerminationReason[]> {
  const { data } = await apiClient.get<TerminationReason[]>(`${EMPLOYEES_BASE}/termination-reasons`, { params: { includeInactive } });
  return data;
}

export async function createTerminationReason(input: TerminationReasonInput): Promise<TerminationReason> {
  const { data } = await apiClient.post<TerminationReason>(`${EMPLOYEES_BASE}/termination-reasons`, input);
  return data;
}

export async function updateTerminationReason(reasonId: string, input: TerminationReasonInput): Promise<TerminationReason> {
  const { data } = await apiClient.put<TerminationReason>(`${EMPLOYEES_BASE}/termination-reasons/${reasonId}`, input);
  return data;
}

export async function setTerminationReasonActive(reasonId: string, active: boolean): Promise<TerminationReason> {
  const { data } = await apiClient.post<TerminationReason>(
    `${EMPLOYEES_BASE}/termination-reasons/${reasonId}/${active ? "reactivate" : "deactivate"}`,
  );
  return data;
}

export async function getCollectiveDismissalStatus(asOf?: string): Promise<CollectiveDismissalStatus> {
  const { data } = await apiClient.get<CollectiveDismissalStatus>(`${EMPLOYEES_BASE}/collective-dismissal-status`, {
    params: asOf ? { asOf } : undefined,
  });
  return data;
}

export async function terminateEmployee(employeeId: string, input: TerminateEmployeeInput): Promise<TerminateEmployeeResult> {
  const { data } = await apiClient.post<TerminateEmployeeResult>(`${EMPLOYEES_BASE}/${employeeId}/terminate`, input);
  return data;
}

export async function getEmploymentPeriods(employeeId: string): Promise<EmploymentHistory> {
  const { data } = await apiClient.get<EmploymentHistory>(`${EMPLOYEES_BASE}/${employeeId}/employment-periods`);
  return data;
}

export async function rehireEmployee(employeeId: string, input: RehireEmployeeInput): Promise<RehireEmployeeResult> {
  const { data } = await apiClient.post<RehireEmployeeResult>(`${EMPLOYEES_BASE}/${employeeId}/rehire`, input);
  return data;
}

// ---------------------------------------------------------------- Union memberships (Sendika bilgileri, E5b)

/** "TİS kapsamı": member, solidarity dues, union-approved coverage, or explicitly out of scope. */
export type UnionCoverageType = "member" | "solidarityDues" | "unionApproved" | "outOfScope";

export const UNION_COVERAGE_TYPES: UnionCoverageType[] = ["member", "solidarityDues", "unionApproved", "outOfScope"];

/** Special-category personal data (KVKK md.6): only returned with employee.union.view, name stored encrypted. */
export interface UnionMembership {
  id: string;
  unionName: string;
  businessLineCode: string | null;
  membershipDate: string;
  /** Collective-agreement entitlement runs from here, not from the membership date. */
  employerNotifiedDate: string | null;
  coverageType: UnionCoverageType;
  isUnionOfficer: boolean;
  officerSince: string | null;
  endDate: string | null;
  isCurrent: boolean;
}

export interface UnionMembershipInput {
  unionName: string;
  membershipDate: string;
  coverageType: UnionCoverageType;
  businessLineCode: string | null;
  employerNotifiedDate: string | null;
  isUnionOfficer: boolean;
  officerSince: string | null;
  endDate: string | null;
}

export async function getUnionMemberships(employeeId: string): Promise<UnionMembership[]> {
  const { data } = await apiClient.get<UnionMembership[]>(`${EMPLOYEES_BASE}/${employeeId}/union-memberships`);
  return data;
}

export async function addUnionMembership(employeeId: string, input: UnionMembershipInput): Promise<UnionMembership[]> {
  const { data } = await apiClient.post<UnionMembership[]>(`${EMPLOYEES_BASE}/${employeeId}/union-memberships`, input);
  return data;
}

export async function updateUnionMembership(employeeId: string, membershipId: string, input: UnionMembershipInput): Promise<UnionMembership[]> {
  const { data } = await apiClient.put<UnionMembership[]>(`${EMPLOYEES_BASE}/${employeeId}/union-memberships/${membershipId}`, input);
  return data;
}

export async function deleteUnionMembership(employeeId: string, membershipId: string): Promise<UnionMembership[]> {
  const { data } = await apiClient.delete<UnionMembership[]>(`${EMPLOYEES_BASE}/${employeeId}/union-memberships/${membershipId}`);
  return data;
}

// ---------------------------------------------------------------- Subcontractors (Alt işveren, E5b)

/** A subcontractor company whose staff work at the tenant's worksite; deactivated rows stay linked to their employees. */
export interface Subcontractor {
  id: string;
  name: string;
  taxNumber: string | null;
  contactName: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
  contractStartDate: string | null;
  contractEndDate: string | null;
  isActive: boolean;
}

export interface SubcontractorInput {
  name: string;
  taxNumber: string | null;
  contactName: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
  contractStartDate: string | null;
  contractEndDate: string | null;
}

export async function getSubcontractors(includeInactive = false): Promise<Subcontractor[]> {
  const { data } = await apiClient.get<Subcontractor[]>(`${EMPLOYEES_BASE}/subcontractors`, { params: { includeInactive } });
  return data;
}

export async function createSubcontractor(input: SubcontractorInput): Promise<Subcontractor> {
  const { data } = await apiClient.post<Subcontractor>(`${EMPLOYEES_BASE}/subcontractors`, input);
  return data;
}

export async function updateSubcontractor(subcontractorId: string, input: SubcontractorInput): Promise<Subcontractor> {
  const { data } = await apiClient.put<Subcontractor>(`${EMPLOYEES_BASE}/subcontractors/${subcontractorId}`, input);
  return data;
}

export async function setSubcontractorActive(subcontractorId: string, active: boolean): Promise<Subcontractor> {
  const { data } = await apiClient.post<Subcontractor>(
    `${EMPLOYEES_BASE}/subcontractors/${subcontractorId}/${active ? "reactivate" : "deactivate"}`,
  );
  return data;
}

/** Links the employee to a subcontractor; null unlinks them (tenant's own staff). */
export async function linkEmployeeSubcontractor(employeeId: string, subcontractorId: string | null): Promise<EmployeeDetail> {
  const { data } = await apiClient.put<EmployeeDetail>(`${EMPLOYEES_BASE}/${employeeId}/subcontractor`, { subcontractorId });
  return data;
}
