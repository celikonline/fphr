/**
 * Notifications service: the caller's own in-app notifications (header bell) and per-channel preferences.
 * Mirrors Sense.Modules.Notifications (MyNotificationsController, UserNotificationDto, NotificationPreferenceDto).
 * Nulls are omitted by the API, so nullable fields are optional here.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/notifications/my";

export interface UserNotificationDto {
  id: string;
  templateKey: string;
  /** Rendered by the server in the recipient's culture. */
  title: string;
  body: string;
  /** App-relative path (e.g. "/app/inbox") the notification opens. */
  actionUrl?: string | null;
  createdAt: string;
  readAt?: string | null;
}

export interface MyNotificationsPageDto {
  items: UserNotificationDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  unreadCount: number;
}

export interface ListMyNotificationsQuery {
  page?: number;
  pageSize?: number;
  unreadOnly?: boolean;
}

export async function listMyNotifications(query: ListMyNotificationsQuery = {}): Promise<MyNotificationsPageDto> {
  const { data } = await apiClient.get<MyNotificationsPageDto>(BASE, { params: query });
  return { ...data, items: data.items ?? [] };
}

export async function getUnreadCount(): Promise<number> {
  const { data } = await apiClient.get<{ count?: number }>(`${BASE}/unread-count`);
  return data.count ?? 0;
}

export async function markNotificationRead(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/read`);
}

export async function markAllNotificationsRead(): Promise<void> {
  await apiClient.post(`${BASE}/read-all`);
}

export type NotificationChannel = "email" | "in_app";
export type NotificationCategory = "account" | "request" | "approval";

export interface NotificationChannelSettingDto {
  channel: NotificationChannel;
  enabled: boolean;
  locked: boolean;
}

export interface NotificationPreferenceDto {
  templateKey: string;
  category: NotificationCategory;
  isMandatory: boolean;
  channels: NotificationChannelSettingDto[];
}

export interface NotificationPreferenceInput {
  templateKey: string;
  channel: NotificationChannel;
  enabled: boolean;
}

export async function getNotificationPreferences(): Promise<NotificationPreferenceDto[]> {
  const { data } = await apiClient.get<NotificationPreferenceDto[]>(`${BASE}/preferences`);
  return data ?? [];
}

export async function updateNotificationPreferences(items: NotificationPreferenceInput[]): Promise<void> {
  await apiClient.put(`${BASE}/preferences`, { items });
}

export const NOTIFICATIONS_QUERY_KEY = ["notifications"] as const;
export const NOTIFICATIONS_UNREAD_KEY = ["notifications", "unread-count"] as const;
export const NOTIFICATION_PREFERENCES_KEY = ["notifications", "preferences"] as const;
export const NOTIFICATION_SETTINGS_PATH = "/app/notifications/settings";
