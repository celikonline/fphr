/**
 * İSG iş kazası / ramak kala kayıtları (senseik-015 Faz 3): mirrors `IncidentsController` (`api/v1/ohs/incidents`).
 * The SGK notification deadline (3rd business day after a work accident) is computed server-side.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/ohs/incidents";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type OhsIncidentKind = "workAccident" | "nearMiss";
export const OHS_INCIDENT_KINDS: OhsIncidentKind[] = ["workAccident", "nearMiss"];

/** `OhsIncidentDto`; DateOnly fields are "yyyy-MM-dd", nulls omitted. */
export interface OhsIncident {
  id: string;
  kind: OhsIncidentKind;
  eventDate: string;
  locationId?: string | null;
  employeeId?: string | null;
  reportedByEmployeeId?: string | null;
  description: string;
  sgkNotificationDeadline?: string | null;
  sgkNotifiedOn?: string | null;
  isSgkOverdue: boolean;
  correctiveAction?: string | null;
  closedOn?: string | null;
}

export interface RecordIncidentInput {
  kind: OhsIncidentKind;
  eventDate: string;
  locationId: string | null;
  employeeId: string | null;
  description: string;
}

export async function listIncidents(): Promise<OhsIncident[]> {
  const { data } = await apiClient.get<OhsIncident[]>(BASE);
  return data;
}

export async function recordIncident(input: RecordIncidentInput): Promise<string> {
  const { data } = await apiClient.post<string>(BASE, input);
  return data;
}

export async function markIncidentSgkNotified(id: string, notifiedOn: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/sgk-notified`, { notifiedOn });
}

export async function closeIncident(id: string, correctiveAction: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/close`, { correctiveAction });
}

export async function listMyIncidentReports(): Promise<OhsIncident[]> {
  const { data } = await apiClient.get<OhsIncident[]>(`${BASE}/my`);
  return data;
}

export async function reportNearMiss(input: { eventDate: string; locationId: string | null; description: string }): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/my`, input);
  return data;
}
