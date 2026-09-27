import * as Notifications from "expo-notifications";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage } from "@/lib/storage";
import { NotificationPayload } from "@/types";

// Set notification handler
Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: true,
    shouldSetBadge: true,
  }),
});

export class PushNotificationService {
  private listenerRef: Notifications.Subscription | null = null;
  private responseListenerRef: Notifications.Subscription | null = null;

  /**
   * `EXPO_PUBLIC_PROJECT_ID` is unset in dev builds (no EAS project configured yet) - this is
   * expected, not an error: permission requests and notification listeners must keep working
   * without a push token, so this resolves to `null` instead of throwing (a thrown error here
   * used to surface as a red LogBox toast on every app start). B7: this used to log via
   * `console.warn`, which RN/Expo also renders as an on-screen LogBox banner covering the tab bar
   * on every launch - skip silently instead (Metro's terminal isn't the right place for an
   * expected, permanent dev-environment condition either).
   */
  async registerPushToken(): Promise<string | null> {
    const projectId = process.env.EXPO_PUBLIC_PROJECT_ID;
    if (!projectId) {
      return null;
    }

    try {
      const token = await Notifications.getExpoPushTokenAsync({ projectId });

      // Never log the push token: it addresses this device and is readable via logcat.

      // Send to backend
      const userId = await SecureStorage.getUserId();
      if (userId) {
        await this.uploadPushToken(userId, token.data);
      }

      return token.data;
    } catch (error) {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Failed to register push token.");
      }
      throw error;
    }
  }

  // BACKEND UCU YOK: `/api/users/{id}/push-tokens` henüz Identity/Employee API'sinde tanımlı değil
  // (ayrı bir iş - bkz. B1). Uç eklenene kadar bu istek her zaman 404 dönecek; sessizce yutulur ki
  // giriş/izin akışı bundan etkilenmesin.
  private async uploadPushToken(userId: string, token: string): Promise<void> {
    try {
      await apiClient.post(`/api/users/${encodeURIComponent(userId)}/push-tokens`, {
        token,
        type: "expo",
      });
    } catch {
      // Don't throw - continue even if backend upload fails. Never logged (B7: console.warn
      // renders as an on-screen LogBox banner in dev, not just a terminal line; the axios error
      // object must never be logged either way, since it can carry auth headers).
    }
  }

  setupNotificationListeners(
    onNotificationReceived?: (notification: Notifications.Notification) => void,
    onNotificationResponse?: (response: Notifications.NotificationResponse) => void
  ): void {
    // Listener for notifications received while app is in foreground
    this.listenerRef = Notifications.addNotificationReceivedListener((notification) => {
      // Never log the notification body: it can contain payroll/leave PII.
      onNotificationReceived?.(notification);
    });

    // Listener for user interactions with notifications
    this.responseListenerRef = Notifications.addNotificationResponseReceivedListener((response) => {
      onNotificationResponse?.(response);
    });
  }

  removeNotificationListeners(): void {
    this.listenerRef?.remove();
    this.responseListenerRef?.remove();
  }

  async requestNotificationPermissions(): Promise<boolean> {
    try {
      const { status } = await Notifications.requestPermissionsAsync();
      return status === "granted";
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Failed to request notification permissions.");
      }
      return false;
    }
  }

  async sendLocalNotification(payload: NotificationPayload): Promise<void> {
    try {
      await Notifications.scheduleNotificationAsync({
        content: {
          title: payload.title,
          body: payload.body,
          data: payload.data || {},
        },
        trigger: { type: Notifications.SchedulableTriggerInputTypes.TIME_INTERVAL, seconds: 1 },
      });
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Failed to send local notification.");
      }
    }
  }
}

export const pushNotificationService = new PushNotificationService();
