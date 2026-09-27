import * as Notifications from "expo-notifications";
import { Platform } from "react-native";
import { apiClient } from "@/lib/apiClient";
import { NotificationPayload } from "@/types";

// Paths mirror MyNotificationsController (Sense.Modules.Notifications.Api) - see
// docs/implementation/push-bildirimleri-tasarim.md §2.
const DEVICES_PATH = "/api/v1/notifications/my/devices";
const DEVICES_REVOKE_PATH = "/api/v1/notifications/my/devices/revoke";

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
  /**
   * Token most recently confirmed as registered with the backend, kept in memory only. Used by
   * `unregisterPushToken` (logout, best-effort revoke). Not persisted: a fresh app launch always
   * re-registers via `registerPushToken` before a revoke could ever be needed.
   */
  private registeredToken: string | null = null;

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

      await this.uploadPushToken(token.data);

      return token.data;
    } catch (error) {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Failed to register push token.");
      }
      throw error;
    }
  }

  /**
   * Upserts this device's token (`POST /devices`, Bearer-authenticated, idempotent - see
   * push-bildirimleri-tasarim.md §2.1). `deviceName` is intentionally omitted: it would require the
   * `expo-device` native module, which isn't a dependency of this app.
   */
  private async uploadPushToken(token: string): Promise<void> {
    try {
      await apiClient.post(DEVICES_PATH, {
        token,
        platform: Platform.OS,
      });
      this.registeredToken = token;
    } catch {
      // Don't throw - continue even if backend upload fails. Never logged (B7: console.warn
      // renders as an on-screen LogBox banner in dev, not just a terminal line; the axios error
      // object must never be logged either way, since it can carry auth headers).
    }
  }

  /**
   * Best-effort revoke of this device's token (`POST /devices/revoke`), called from
   * `AuthService.logout()` while the Bearer token is still valid. No-op if this device never
   * successfully registered a token. Never throws - logout must complete regardless.
   */
  async unregisterPushToken(): Promise<void> {
    const token = this.registeredToken;
    if (!token) {
      return;
    }

    try {
      await apiClient.post(DEVICES_REVOKE_PATH, { token });
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
    }
  }

  /** Taps on a notification are handled by `usePushTapNavigation`, not here. */
  setupNotificationListeners(
    onNotificationReceived?: (notification: Notifications.Notification) => void
  ): void {
    // Listener for notifications received while app is in foreground
    this.listenerRef = Notifications.addNotificationReceivedListener((notification) => {
      // Never log the notification body: it can contain payroll/leave PII.
      onNotificationReceived?.(notification);
    });
  }

  removeNotificationListeners(): void {
    this.listenerRef?.remove();
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
