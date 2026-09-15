import * as Notifications from "expo-notifications";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage } from "@/lib/storage";
import { NotificationPayload } from "@/types";

// Set notification handler
Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowAlert: true,
    shouldPlaySound: true,
    shouldSetBadge: true
  })
});

export class PushNotificationService {
  private listenerRef: Notifications.Subscription | null = null;
  private responseListenerRef: Notifications.Subscription | null = null;

  async registerPushToken(): Promise<string> {
    try {
      const token = await Notifications.getExpoPushTokenAsync({
        projectId: process.env.EXPO_PUBLIC_PROJECT_ID
      });

      console.log("Push token:", token.data);

      // Send to backend
      const userId = await SecureStorage.getUserId();
      if (userId) {
        await this.uploadPushToken(userId, token.data);
      }

      return token.data;
    } catch (error) {
      console.error("Failed to register push token:", error);
      throw error;
    }
  }

  private async uploadPushToken(userId: string, token: string): Promise<void> {
    try {
      await apiClient.post(`/api/users/${userId}/push-tokens`, {
        token,
        type: "expo"
      });
    } catch (error) {
      console.error("Failed to upload push token to backend:", error);
      // Don't throw - continue even if backend upload fails
    }
  }

  setupNotificationListeners(
    onNotificationReceived?: (notification: Notifications.Notification) => void,
    onNotificationResponse?: (response: Notifications.NotificationResponse) => void
  ): void {
    // Listener for notifications received while app is in foreground
    this.listenerRef = Notifications.addNotificationReceivedListener(notification => {
      console.log("Notification received:", notification);
      onNotificationReceived?.(notification);
    });

    // Listener for user interactions with notifications
    this.responseListenerRef = Notifications.addNotificationResponseReceivedListener(response => {
      console.log("Notification response:", response);
      onNotificationResponse?.(response);
    });
  }

  removeNotificationListeners(): void {
    if (this.listenerRef) {
      Notifications.removeNotificationSubscription(this.listenerRef);
    }
    if (this.responseListenerRef) {
      Notifications.removeNotificationSubscription(this.responseListenerRef);
    }
  }

  async requestNotificationPermissions(): Promise<boolean> {
    try {
      const { status } = await Notifications.requestPermissionsAsync();
      return status === "granted";
    } catch (error) {
      console.error("Failed to request notification permissions:", error);
      return false;
    }
  }

  async sendLocalNotification(payload: NotificationPayload): Promise<void> {
    try {
      await Notifications.scheduleNotificationAsync({
        content: {
          title: payload.title,
          body: payload.body,
          data: payload.data || {}
        },
        trigger: { seconds: 1 }
      });
    } catch (error) {
      console.error("Failed to send local notification:", error);
    }
  }
}

export const pushNotificationService = new PushNotificationService();
