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
  /** E4 (B14): ISO instants (UTC). All-day events carry midnight-UTC instants. */
  startsAt?: string | null;
  endsAt?: string | null;
  isAllDay?: boolean;
  mode?: CompanyEventMode | null;
  location?: string | null;
  onlineUrl?: string | null;
  categoryId?: string | null;
  inviteeEmployeeIds?: string[] | null;
}

export type CompanyEventMode = "onsite" | "online";

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
  startsAt?: string | null;
  endsAt?: string | null;
  isAllDay?: boolean | null;
  mode?: CompanyEventMode | null;
  location?: string | null;
  onlineUrl?: string | null;
  notifyInvitees?: boolean;
  categoryId?: string | null;
  inviteeEmployeeIds?: string[] | null;
}

/** PUT is a full replacement (B14): every field must be sent or it is reset. */
export type UpdateCompanyEventInput = CreateCompanyEventInput;

export async function createCompanyEvent(input: CreateCompanyEventInput): Promise<string> {
  const { data } = await apiClient.post<string>(EVENTS_BASE, input);
  return data;
}

export async function deleteCompanyEvent(id: string): Promise<void> {
  await apiClient.delete(`${EVENTS_BASE}/${id}`);
}

export async function updateCompanyEvent(id: string, input: UpdateCompanyEventInput): Promise<void> {
  await apiClient.put(`${EVENTS_BASE}/${id}`, input);
}

/** `CompanyEventCategoryDto`: name + `#RRGGBB` colour. */
export interface CompanyEventCategory {
  id: string;
  name: string;
  color: string;
}

export interface CompanyEventCategoryInput {
  name: string;
  color: string;
}

export async function listCompanyEventCategories(): Promise<CompanyEventCategory[]> {
  const { data } = await apiClient.get<CompanyEventCategory[]>(`${EVENTS_BASE}/categories`);
  return data;
}

export async function createCompanyEventCategory(input: CompanyEventCategoryInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${EVENTS_BASE}/categories`, input);
  return data;
}

export async function updateCompanyEventCategory(id: string, input: CompanyEventCategoryInput): Promise<void> {
  await apiClient.put(`${EVENTS_BASE}/categories/${id}`, input);
}

/** Rejected with 409 (`company_event_category.in_use`) while events still reference the category. */
export async function deleteCompanyEventCategory(id: string): Promise<void> {
  await apiClient.delete(`${EVENTS_BASE}/categories/${id}`);
}
