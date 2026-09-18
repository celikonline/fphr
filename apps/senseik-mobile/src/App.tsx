import React, { useEffect } from "react";
import { StatusBar } from "expo-status-bar";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { RootNavigator } from "@/navigation/RootNavigator";
import { pushNotificationService } from "@/services/PushNotificationService";

/** Tabs a push notification may request. Anything else is ignored. */
const ALLOWED_PUSH_SCREENS = new Set(["Dashboard", "Attendance", "Leave", "Payroll", "Approvals", "Profile"]);

/**
 * Last validated push-navigation request. RootNavigator consumes and clears it after
 * authentication is confirmed, so an unauthenticated cold start can't land inside the app.
 */
export let pushNavigationTarget: string | null = null;
export function consumePushNavigationTarget(): string | null {
  const target = pushNavigationTarget;
  pushNavigationTarget = null;
  return target;
}

export default function App() {
  useEffect(() => {
    const initializeApp = async () => {
      try {
        // Request notification permissions
        const granted = await pushNotificationService.requestNotificationPermissions();
        if (granted) {
          // Register push token
          await pushNotificationService.registerPushToken();
        }

        // Setup notification listeners. Notification bodies are never logged (PII).
        pushNotificationService.setupNotificationListeners(
          undefined,
          response => {
            // `data.screen` originates from the push sender: only allow known tab
            // screens so a compromised sender can't deep-link into arbitrary routes.
            const screen = response.notification.request.content.data?.screen;
            if (typeof screen === "string" && ALLOWED_PUSH_SCREENS.has(screen)) {
              pushNavigationTarget = screen;
            }
          }
        );
      } catch (error) {
        if (__DEV__) {
          console.error("Failed to initialize app:", error);
        }
      }
    };

    initializeApp();

    return () => {
      pushNotificationService.removeNotificationListeners();
    };
  }, []);

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <StatusBar style="light" />
      <RootNavigator />
    </GestureHandlerRootView>
  );
}
