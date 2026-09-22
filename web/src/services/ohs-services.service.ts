/**
 * İSG hizmetleri (senseik-015 Faz 2): mirrors `OhsServicesController` (`api/v1/ohs/services`).
 * Service minutes, certificate-class and committee norms are evaluated server-side per classified location.
 */
import { apiClient } from "@senseik/api-client";
import type { HazardClass } from "@/services/ohs.service";

const BASE = "/v1/ohs/services";

/** Native C# enums, camelCase JsonStringEnumConverter. */
export type SafetyServiceRole = "safetyExpert" | "workplacePhysician" | "otherHealthPersonnel";
export const SAFETY_SERVICE_ROLES: SafetyServiceRole[] = [
  "safetyExpert",
  "workplacePhysician",
  "otherHealthPersonnel",
];

export type CertificateClass = "a" | "b" | "c";
export const CERTIFICATE_CLASSES: CertificateClass[] = ["a", "b", "c"];

export type CommitteeRole =
  | "chair"
  | "secretary"
  | "physician"
  | "hrRepresentative"
  | "civilDefenseExpert"
  | "foreman"
  | "employeeRepresentative";
export const COMMITTEE_ROLES: CommitteeRole[] = [
  "chair",
  "secretary",
  "physician",
  "hrRepresentative",
  "civilDefenseExpert",
  "foreman",
  "employeeRepresentative",
];

/** `SafetyAssignmentDto`; nulls omitted. */
export interface SafetyAssignment {
  id: string;
  locationId: string;
  role: SafetyServiceRole;
  personName: string;
  employeeId?: string | null;
  certificateClass?: CertificateClass | null;
  certificateNumber?: string | null;
  providerName?: string | null;
  monthlyMinutes: number;
  startDate: string;
  endDate?: string | null;
}

/** `CommitteeMemberDto`. */
export interface CommitteeMember {
  id: string;
  locationId: string;
  role: CommitteeRole;
  personName: string;
  employeeId?: string | null;
  startDate: string;
  endDate?: string | null;
}

export interface ServiceMinutes {
  role: SafetyServiceRole;
  requiredMinutes: number;
  assignedMinutes: number;
  isMet: boolean;
}

/** `LocationServiceSummaryDto`: issues are codes translated under `ohs:services.issues.*`. */
export interface LocationServiceSummary {
  locationId: string;
  hazardClass: HazardClass;
  employeeCount: number;
  minutes: ServiceMinutes[];
  committeeRequired: boolean;
  committeeMeetingIntervalMonths: number;
  committeeMemberCount: number;
  requiredRepresentativeCount: number;
  representativeCount: number;
  issues: string[];
}

export interface CreateSafetyAssignmentInput {
  locationId: string;
  role: SafetyServiceRole;
  personName: string;
  employeeId: string | null;
  certificateClass: CertificateClass | null;
  certificateNumber: string | null;
  providerName: string | null;
  monthlyMinutes: number;
  startDate: string;
}

export interface AddCommitteeMemberInput {
  locationId: string;
  role: CommitteeRole;
  personName: string;
  employeeId: string | null;
  startDate: string;
}

export async function listLocationServiceSummaries(): Promise<LocationServiceSummary[]> {
  const { data } = await apiClient.get<LocationServiceSummary[]>(`${BASE}/locations`);
  return data;
}

export async function listSafetyAssignments(): Promise<SafetyAssignment[]> {
  const { data } = await apiClient.get<SafetyAssignment[]>(`${BASE}/assignments`);
  return data;
}

export async function createSafetyAssignment(input: CreateSafetyAssignmentInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/assignments`, input);
  return data;
}

export async function endSafetyAssignment(id: string, endDate: string): Promise<void> {
  await apiClient.post(`${BASE}/assignments/${id}/end`, { endDate });
}

export async function listCommitteeMembers(): Promise<CommitteeMember[]> {
  const { data } = await apiClient.get<CommitteeMember[]>(`${BASE}/committee`);
  return data;
}

export async function addCommitteeMember(input: AddCommitteeMemberInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/committee`, input);
  return data;
}

export async function endCommitteeMember(id: string, endDate: string): Promise<void> {
  await apiClient.post(`${BASE}/committee/${id}/end`, { endDate });
}
