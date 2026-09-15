/**
 * Notifications service: the caller's own in-app notifications (bell / home) and e-mail/in-app preferences.
 * Mirrors Sense.Modules.Notifications (NotificationItemDto, NotificationPreferenceDto). Nulls are omitted by the API.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

const BASE = "/v1/notifications";

export interface NotificationItemDto {
  id: string;
  templateKey: string;
  category: string;
  /** Server-rendered fallback text (default culture). */
  title: string;
  body: string;
  args?: Record<string, string>;
  link?: string;
  createdAt: string;
  readAt?: string;
}

export interface ListNotificationsQuery {
  page?: number;
  pageSize?: number;
  unreadOnly?: boolean;
}

export async function listNotifications(query: ListNotificationsQuery = {}): Promise<PagedResult<NotificationItemDto>> {
  const { data } = await apiClient.get<PagedResult<NotificationItemDto>>(BASE, { params: query });
  return data;
}

export async function getUnreadCount(): Promise<number> {
  const { data } = await apiClient.get<{ count: number }>(`${BASE}/unread-count`);
  return data.count;
}

export async function markNotificationRead(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/read`);
}

export async function markAllNotificationsRead(): Promise<number> {
  const { data } = await apiClient.post<number>(`${BASE}/read-all`);
  return data;
}

export interface NotificationPreferenceDto {
  templateKey: string;
  category: string;
  emailEnabled: boolean;
  inAppEnabled: boolean;
  isMandatory: boolean;
}

export interface NotificationPreferenceInput {
  templateKey: string;
  emailEnabled: boolean;
  inAppEnabled: boolean;
}

export async function getNotificationPreferences(): Promise<NotificationPreferenceDto[]> {
  const { data } = await apiClient.get<NotificationPreferenceDto[]>(`${BASE}/preferences`);
  return data;
}

export async function updateNotificationPreferences(preferences: NotificationPreferenceInput[]): Promise<void> {
  await apiClient.put(`${BASE}/preferences`, { preferences });
}

export const NOTIFICATIONS_QUERY_KEY = ["notifications"] as const;
export const NOTIFICATIONS_UNREAD_KEY = ["notifications", "unread-count"] as const;
export const NOTIFICATION_PREFERENCES_KEY = ["notifications", "preferences"] as const;
