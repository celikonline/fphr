/**
 * Şirket etkinlik takvimi (senseik-027, roadmap #71/P-19): mirrors `CompanyEventsController.cs`
 * (`api/v1/announcements/events`). `getCompanyCalendar` merges manual events with derived Birthday/LeaveStart
 * entries; the derived entries have no `id` (nothing to edit/delete) and carry `employeeId` instead.
 */
import { apiClient } from "@senseik/api-client";

const EVENTS_BASE = "/v1/announcements/events";

export type CompanyEventType = "companyEvent" | "meeting" | "birthday" | "leaveStart";

/** `CompanyCalendarEntryDto`. Only `companyEvent`/`meeting` entries can be created or deleted. */
export interface CompanyCalendarEntry {
  id: string | null;
  title: string;
  description?: string | null;
  date: string;
  type: CompanyEventType;
  visibilityOrgUnitId?: string | null;
  employeeId?: string | null;
  createdByUserId?: string | null;
}

export interface CompanyCalendarQuery {
  from: string;
  to: string;
  orgUnitId?: string | null;
}

export async function getCompanyCalendar(query: CompanyCalendarQuery): Promise<CompanyCalendarEntry[]> {
  const { data } = await apiClient.get<CompanyCalendarEntry[]>(`${EVENTS_BASE}/calendar`, {
    params: { from: query.from, to: query.to, ...(query.orgUnitId ? { orgUnitId: query.orgUnitId } : {}) },
  });
  return data;
}

export interface CreateCompanyEventInput {
  title: string;
  description?: string | null;
  eventDate: string;
  type: Extract<CompanyEventType, "companyEvent" | "meeting">;
  visibilityOrgUnitId?: string | null;
}

export async function createCompanyEvent(input: CreateCompanyEventInput): Promise<string> {
  const { data } = await apiClient.post<string>(EVENTS_BASE, input);
  return data;
}

export async function deleteCompanyEvent(id: string): Promise<void> {
  await apiClient.delete(`${EVENTS_BASE}/${id}`);
}
