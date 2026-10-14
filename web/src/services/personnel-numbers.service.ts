import { apiClient } from "@senseik/api-client";

const BASE = "/v1/employees";
export type AssignmentMode = "internal" | "external";
export type PersonnelOperation = "hire" | "import";
export interface PersonnelNumberRange {
  id: string;
  code: string;
  name: string;
  startNumber: string;
  endNumber: string;
  assignmentMode: AssignmentMode;
  lastAssignedNumber?: string;
  isActive: boolean;
}
export interface PersonnelNumberRule {
  id: string;
  companyId: string;
  companyName: string;
  operation: PersonnelOperation;
  rangeId: string;
  rangeCode: string;
  assignmentMode: AssignmentMode;
  isActive: boolean;
  personnelAreaId?: string;
  personnelAreaName?: string;
}
export interface PersonnelNumberAssignment {
  configured: boolean;
  companyId?: string;
  companyName?: string;
  rangeId?: string;
  rangeCode?: string;
  assignmentMode?: AssignmentMode;
  startNumber?: string;
  endNumber?: string;
  nextNumber?: string;
  personnelAreaId?: string;
  personnelAreaName?: string;
}
export const isPersonnelNumber = (value: string) =>
  /^[0-9]{8}$/.test(value) && value !== "00000000";
export function acceptsPersonnelNumber(value: string, assignment: PersonnelNumberAssignment) {
  if (!assignment.configured) return value.length <= 30;
  return (
    assignment.assignmentMode === "external" &&
    isPersonnelNumber(value) &&
    !!assignment.startNumber &&
    !!assignment.endNumber &&
    value >= assignment.startNumber &&
    value <= assignment.endNumber
  );
}
export function remainingPersonnelNumbers(range: PersonnelNumberRange) {
  return Math.max(
    0,
    Number(range.endNumber) -
      Number(range.lastAssignedNumber ?? range.startNumber) +
      (range.lastAssignedNumber ? 0 : 1)
  );
}
export async function getPersonnelNumberRanges(): Promise<PersonnelNumberRange[]> {
  return (
    await apiClient.get(`${BASE}/personnel-number-ranges`, { params: { includeInactive: true } })
  ).data;
}
export async function savePersonnelNumberRange(
  input: Omit<PersonnelNumberRange, "id" | "lastAssignedNumber" | "isActive">,
  id?: string
) {
  if (id)
    return (
      await apiClient.put(`${BASE}/personnel-number-ranges/${id}`, {
        name: input.name,
        endNumber: input.endNumber,
      })
    ).data;
  return (await apiClient.post(`${BASE}/personnel-number-ranges`, input)).data;
}
export async function setPersonnelNumberRangeActive(id: string, active: boolean) {
  await apiClient.post(
    `${BASE}/personnel-number-ranges/${id}/${active ? "reactivate" : "deactivate"}`
  );
}
export async function getPersonnelNumberRules(): Promise<PersonnelNumberRule[]> {
  return (await apiClient.get(`${BASE}/personnel-number-rules`)).data;
}
export async function savePersonnelNumberRule(
  input: {
    companyId: string;
    personnelAreaId?: string | null;
    operation: PersonnelOperation;
    rangeId: string;
    isActive: boolean;
  },
  id?: string
) {
  if (id)
    return (
      await apiClient.put(`${BASE}/personnel-number-rules/${id}`, {
        rangeId: input.rangeId,
        isActive: input.isActive,
      })
    ).data;
  return (
    await apiClient.post(`${BASE}/personnel-number-rules`, {
      companyId: input.companyId,
      personnelAreaId: input.personnelAreaId,
      operation: input.operation,
      rangeId: input.rangeId,
    })
  ).data;
}
export async function getPersonnelNumberCompanies(): Promise<
  { id: string; name: string; personnelAreas: { id: string; name: string }[] }[]
> {
  return (await apiClient.get(`${BASE}/personnel-number-companies`)).data;
}
export async function getPersonnelNumberAssignment(
  orgUnitId: string | null
): Promise<PersonnelNumberAssignment> {
  return (
    await apiClient.get(`${BASE}/personnel-number-assignment`, {
      params: { orgUnitId: orgUnitId ?? undefined, operation: "hire" },
    })
  ).data;
}
