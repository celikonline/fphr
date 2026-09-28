import { apiClient } from "@/lib/apiClient";

/**
 * N1: the caller's own in-app notifications (bell / notifications screen) and channel preferences.
 * Mirrors `MyNotificationsController` (Sense.Modules.Notifications.Api) - the same endpoints
 * `web/src/services/notifications.service.ts` calls. Nulls are omitted by the API (see project
 * conventions), so nullable fields are optional here.
 */
const NOTIFICATIONS_BASE = "/api/v1/notifications/my";

export interface UserNotification {
  id: string;
  templateKey: string;
  /** Rendered by the server in the caller's culture - no client-side translation needed. */
  title: string;
  body: string;
  /** App-relative web path (e.g. "/app/onboarding"); resolved to a mobile screen via `resolveNotificationNavigationTarget` - never navigated to directly. */
  actionUrl?: string | null;
  createdAt: string;
  readAt?: string | null;
}

export interface MyNotificationsPage {
  items: UserNotification[];
  page: number;
  pageSize: number;
  totalCount: number;
  unreadCount: number;
}

export interface ListMyNotificationsParams {
  page?: number;
  pageSize?: number;
  unreadOnly?: boolean;
}

export type NotificationChannel = "email" | "in_app";
export type NotificationCategory =
  | "account"
  | "request"
  | "approval"
  | "reminder"
  | "employee"
  | "announcement"
  | "payroll"
  | "onboarding";

export interface NotificationChannelSetting {
  channel: NotificationChannel;
  enabled: boolean;
  locked: boolean;
}

export interface NotificationPreference {
  templateKey: string;
  category: NotificationCategory;
  isMandatory: boolean;
  channels: NotificationChannelSetting[];
}

export interface NotificationPreferenceInput {
  templateKey: string;
  channel: NotificationChannel;
  enabled: boolean;
}

export class NotificationsService {
  async listMyNotifications(params: ListMyNotificationsParams = {}): Promise<MyNotificationsPage> {
    const response = await apiClient.get<MyNotificationsPage>(NOTIFICATIONS_BASE, { params });
    return { ...response.data, items: response.data.items ?? [] };
  }

  async getUnreadCount(): Promise<number> {
    const response = await apiClient.get<{ count?: number }>(`${NOTIFICATIONS_BASE}/unread-count`);
    return response.data.count ?? 0;
  }

  async markNotificationRead(id: string): Promise<void> {
    await apiClient.post(`${NOTIFICATIONS_BASE}/${id}/read`);
  }

  async markAllNotificationsRead(): Promise<void> {
    await apiClient.post(`${NOTIFICATIONS_BASE}/read-all`);
  }

  async getPreferences(): Promise<NotificationPreference[]> {
    const response = await apiClient.get<NotificationPreference[]>(
      `${NOTIFICATIONS_BASE}/preferences`
    );
    return response.data ?? [];
  }

  async updatePreferences(items: NotificationPreferenceInput[]): Promise<void> {
    await apiClient.put(`${NOTIFICATIONS_BASE}/preferences`, { items });
  }
}

export const notificationsService = new NotificationsService();
