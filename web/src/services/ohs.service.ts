/**
 * Occupational Safety module (İSG, senseik-015, Faz 0-1): mirrors `HazardClassificationsController`/
 * `RiskAssessmentsController` (`api/v1/ohs/...`). Committee, accident/near-miss, PPE and document-engine
 * integration are separate, later phases - not covered here yet.
 */
import { apiClient } from "@senseik/api-client";

const HAZARD_CLASSIFICATIONS_BASE = "/v1/ohs/hazard-classifications";
const RISK_ASSESSMENTS_BASE = "/v1/ohs/risk-assessments";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type HazardClass = "low" | "medium" | "high";
export const HAZARD_CLASSES: HazardClass[] = ["low", "medium", "high"];

export interface HazardClassification {
  id: string;
  locationId: string;
  hazardClass: HazardClass;
  effectiveFrom: string;
  note: string | null;
}

export interface RiskAssessment {
  id: string;
  locationId: string;
  hazardClassAtAssessment: HazardClass;
  assessmentDate: string;
  renewalPeriodMonths: number;
  nextDueDate: string;
  preparedBy: string;
  note: string | null;
}

/** The hazard classification in effect today, one row per location that has ever been classified. */
export async function listCurrentHazardClassifications(): Promise<HazardClassification[]> {
  const { data } = await apiClient.get<HazardClassification[]>(HAZARD_CLASSIFICATIONS_BASE);
  return data;
}

export async function listHazardClassificationHistory(locationId: string): Promise<HazardClassification[]> {
  const { data } = await apiClient.get<HazardClassification[]>(`${HAZARD_CLASSIFICATIONS_BASE}/locations/${locationId}/history`);
  return data;
}

export async function setHazardClassification(
  locationId: string,
  hazardClass: HazardClass,
  effectiveFrom: string,
  note: string | null,
): Promise<string> {
  const { data } = await apiClient.post<string>(HAZARD_CLASSIFICATIONS_BASE, { locationId, hazardClass, effectiveFrom, note });
  return data;
}

export async function listRiskAssessments(): Promise<RiskAssessment[]> {
  const { data } = await apiClient.get<RiskAssessment[]>(RISK_ASSESSMENTS_BASE);
  return data;
}

export async function recordRiskAssessment(
  locationId: string,
  assessmentDate: string,
  preparedBy: string,
  note: string | null,
): Promise<string> {
  const { data } = await apiClient.post<string>(RISK_ASSESSMENTS_BASE, { locationId, assessmentDate, preparedBy, note });
  return data;
}
