/**
 * Şirket yapısı service (Faz 0 items 3–6): hierarchy levels (OrgUnitType), org units, companies,
 * holiday calendars and unit authorizations ("Birim yetkilendirme").
 *
 * Wired to the real Organization/Identity endpoints; shapes mirror the backend DTOs
 * (OrgUnitTypeDto, OrgUnitDto, CompanyDto, HolidayCalendarDto, UnitAuthorizationDto, ...).
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

const ORG_BASE = "/v1/organization";
const UNIT_TYPES = `${ORG_BASE}/unit-types`;
const UNITS = `${ORG_BASE}/units`;
const COMPANIES = `${ORG_BASE}/companies`;
const HOLIDAY_CALENDARS = `${ORG_BASE}/holiday-calendars`;
const UNIT_AUTHORIZATIONS = "/v1/identity/unit-authorizations";

export const COMPANY_TYPE_CODE = "Company";
export const BRANCH_TYPE_CODE = "Branch";

// ---------------------------------------------------------------- Unit types (levels)

export interface OrgUnitType {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  isSystem: boolean;
  allowsNesting: boolean;
}

export async function listUnitTypes(activeOnly = false): Promise<OrgUnitType[]> {
  const { data } = await apiClient.get<OrgUnitType[]>(UNIT_TYPES, { params: { activeOnly } });
  return data;
}

export async function createUnitType(input: { name: string; insertAfterTypeId: string | null }): Promise<OrgUnitType> {
  const { data } = await apiClient.post<OrgUnitType>(UNIT_TYPES, input);
  return data;
}

export async function activateUnitType(id: string, insertAfterTypeId: string | null): Promise<OrgUnitType> {
  const { data } = await apiClient.post<OrgUnitType>(`${UNIT_TYPES}/${id}/activate`, { insertAfterTypeId });
  return data;
}

export async function renameUnitType(id: string, name: string): Promise<OrgUnitType> {
  const { data } = await apiClient.put<OrgUnitType>(`${UNIT_TYPES}/${id}`, { name });
  return data;
}

export async function reorderUnitTypes(orderedTypeIds: string[]): Promise<OrgUnitType[]> {
  const { data } = await apiClient.put<OrgUnitType[]>(`${UNIT_TYPES}/order`, { orderedTypeIds });
  return data;
}

export async function deleteUnitType(id: string): Promise<void> {
  await apiClient.delete(`${UNIT_TYPES}/${id}`);
}

// ---------------------------------------------------------------- Org units

export interface OrgUnit {
  id: string;
  typeId: string;
  parentId: string | null;
  name: string;
  managerEmployeeId: string | null;
  locationId: string | null;
  sortOrder: number;
  isActive: boolean;
}

export interface OrgUnitCreateInput {
  typeId: string;
  parentId: string | null;
  name: string;
  managerEmployeeId: string | null;
  locationId: string | null;
  sortOrder?: number;
}

export interface OrgUnitUpdateInput {
  name: string;
  parentId: string | null;
  managerEmployeeId: string | null;
  locationId: string | null;
  sortOrder: number;
}

export async function listUnits(activeOnly = true): Promise<OrgUnit[]> {
  const { data } = await apiClient.get<OrgUnit[]>(UNITS, { params: { activeOnly } });
  return data;
}

export async function createUnit(input: OrgUnitCreateInput): Promise<OrgUnit> {
  const { data } = await apiClient.post<OrgUnit>(UNITS, { sortOrder: 0, ...input });
  return data;
}

export async function updateUnit(id: string, input: OrgUnitUpdateInput): Promise<OrgUnit> {
  const { data } = await apiClient.put<OrgUnit>(`${UNITS}/${id}`, input);
  return data;
}

export async function deactivateUnit(id: string): Promise<void> {
  await apiClient.delete(`${UNITS}/${id}`);
}

// ---------------------------------------------------------------- Companies

export interface Company {
  id: string;
  orgUnitId: string;
  /** Contract (P3) extension; may be missing on older backends - fall back to the org unit name. */
  name?: string;
  holidayCalendarId: string | null;
  holidayCalendarName?: string | null;
  jointEmploymentGroupCode: string | null;
}

export async function listCompanies(): Promise<Company[]> {
  const { data } = await apiClient.get<Company[]>(COMPANIES);
  return data;
}

export async function createCompany(input: { name: string; holidayCalendarId: string | null }): Promise<Company> {
  const { data } = await apiClient.post<Company>(COMPANIES, input);
  return data;
}

export async function assignCompanyHolidayCalendar(id: string, holidayCalendarId: string | null): Promise<Company> {
  const { data } = await apiClient.put<Company>(`${COMPANIES}/${id}/holiday-calendar`, { holidayCalendarId });
  return data;
}

// ---------------------------------------------------------------- Holiday calendars

export interface HolidayCalendar {
  id: string;
  name: string;
  countryCode: string;
  isSystemDefined: boolean;
}

export interface HolidayCalendarEntry {
  id: string;
  holidayCalendarId: string;
  /** DateOnly "yyyy-MM-dd". */
  date: string;
  name: string;
  isRecurringAnnually: boolean;
}

export async function listHolidayCalendars(): Promise<HolidayCalendar[]> {
  const { data } = await apiClient.get<HolidayCalendar[]>(HOLIDAY_CALENDARS);
  return data;
}

export async function createHolidayCalendar(input: { name: string; countryCode: string }): Promise<HolidayCalendar> {
  const { data } = await apiClient.post<HolidayCalendar>(HOLIDAY_CALENDARS, input);
  return data;
}

export async function listHolidayCalendarEntries(calendarId: string): Promise<HolidayCalendarEntry[]> {
  const { data } = await apiClient.get<HolidayCalendarEntry[]>(`${HOLIDAY_CALENDARS}/${calendarId}/entries`);
  return data;
}

export async function addHolidayCalendarEntry(
  calendarId: string,
  input: { date: string; name: string; isRecurringAnnually: boolean },
): Promise<HolidayCalendarEntry> {
  const { data } = await apiClient.post<HolidayCalendarEntry>(`${HOLIDAY_CALENDARS}/${calendarId}/entries`, input);
  return data;
}

/** Default calendar for new companies: the seeded system TR calendar, else the first system one. */
export function pickDefaultHolidayCalendar(calendars: HolidayCalendar[]): HolidayCalendar | undefined {
  const system = calendars.filter((c) => c.isSystemDefined);
  return system.find((c) => c.countryCode.toUpperCase() === "TR") ?? system[0] ?? calendars[0];
}

// ---------------------------------------------------------------- Unit approval policies ("Opsiyonel alanlar")

/** Request types a unit can carry an approval policy for; mirrors Organization.Contracts.ApprovalRequestTypes.All. */
export const APPROVAL_REQUEST_TYPES = ["Leave", "Advance", "Expense", "Overtime"] as const;

export type ApprovalRequestType = (typeof APPROVAL_REQUEST_TYPES)[number];

/** Mirrors `OrgUnitApprovalPolicyDto`; nulls are omitted by the API, so treat missing as null. */
export interface OrgUnitApprovalPolicy {
  requestType: string;
  /** The unit's own assignment; missing = inherits from an ancestor. */
  approvalPolicyId?: string | null;
  /** Own assignment, else the nearest active ancestor's; missing = Workflow default (first manager). */
  effectiveApprovalPolicyId?: string | null;
  /** Ancestor supplying the effective policy; missing when the policy is the unit's own (or there is none). */
  inheritedFromOrgUnitId?: string | null;
}

export interface OrgUnitApprovalPolicyInput {
  requestType: string;
  /** null clears the unit's own assignment (it inherits again). */
  approvalPolicyId: string | null;
}

export async function listUnitApprovalPolicies(unitId: string): Promise<OrgUnitApprovalPolicy[]> {
  const { data } = await apiClient.get<OrgUnitApprovalPolicy[]>(`${UNITS}/${unitId}/approval-policies`);
  return data;
}

export async function setUnitApprovalPolicies(
  unitId: string,
  assignments: OrgUnitApprovalPolicyInput[],
): Promise<OrgUnitApprovalPolicy[]> {
  const { data } = await apiClient.put<OrgUnitApprovalPolicy[]>(`${UNITS}/${unitId}/approval-policies`, { assignments });
  return data;
}

/** Workflow-owned approval policy option. */
export interface ApprovalPolicyOption {
  id: string;
  name: string;
}

/**
 * Policy options come from the Workflow module, which is not built yet: a 404/403/network error means "no Workflow",
 * and the caller renders the selects disabled with a hint instead of failing.
 */
export async function listApprovalPolicies(): Promise<ApprovalPolicyOption[]> {
  const { data } = await apiClient.get<ApprovalPolicyOption[]>("/v1/workflow/approval-policies");
  return data;
}

// ---------------------------------------------------------------- Unit authorizations

export interface UnitAuthorization {
  id: string;
  orgUnitId: string;
  userId: string;
  canViewSalaryAndPayments: boolean;
  canViewUnitReports: boolean;
  grantedByUserId: string | null;
  grantedAt: string;
  isActive: boolean;
  /** Contract (P2) extensions. */
  userFullName?: string;
  userEmail?: string;
  employeeId?: string | null;
}

export interface UnitAuthorizationCandidate {
  userId: string;
  fullName: string;
  email: string;
  employeeId: string | null;
}

export interface GrantUnitAuthorizationInput {
  orgUnitId: string;
  userId: string;
  canViewSalaryAndPayments: boolean;
  canViewUnitReports: boolean;
}

export async function listUnitAuthorizations(orgUnitId: string): Promise<UnitAuthorization[]> {
  const { data } = await apiClient.get<UnitAuthorization[]>(UNIT_AUTHORIZATIONS, { params: { orgUnitId } });
  return data;
}

export async function searchUnitAuthorizationCandidates(q: string): Promise<UnitAuthorizationCandidate[]> {
  const { data } = await apiClient.get<UnitAuthorizationCandidate[]>(`${UNIT_AUTHORIZATIONS}/candidates`, {
    params: { q: q || undefined },
  });
  return data;
}

/** Upsert: the backend keys grants by (orgUnitId, userId). */
export async function grantUnitAuthorization(input: GrantUnitAuthorizationInput): Promise<string> {
  const { data } = await apiClient.post<string>(UNIT_AUTHORIZATIONS, input);
  return data;
}

export async function revokeUnitAuthorization(id: string): Promise<void> {
  await apiClient.delete(`${UNIT_AUTHORIZATIONS}/${id}`);
}

// ---------------------------------------------------------------- Employee lookup (manager picker)

export interface EmployeeOption {
  id: string;
  displayName: string;
  workEmail?: string | null;
}

export async function searchEmployees(q: string): Promise<EmployeeOption[]> {
  const { data } = await apiClient.get<PagedResult<EmployeeOption>>("/v1/employees", {
    params: { q: q || undefined, page: 1, pageSize: 20, sort: "displayName" },
  });
  return data.items;
}

export async function getEmployeeOption(id: string): Promise<EmployeeOption> {
  const { data } = await apiClient.get<EmployeeOption>(`/v1/employees/${id}`);
  return data;
}
