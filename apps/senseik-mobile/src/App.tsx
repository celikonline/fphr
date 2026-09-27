import React, { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { RootNavigator } from "@/navigation/RootNavigator";
import { pushNotificationService } from "@/services/PushNotificationService";
import { resolvePushNavigationScreen } from "@/lib/pushNavigation";
import { useAuthStore } from "@/store/authStore";

/** How long the foreground in-app banner stays visible before auto-dismissing. */
const FOREGROUND_BANNER_DURATION_MS = 4000;

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

interface ForegroundBanner {
  title: string;
  body: string;
}

export default function App() {
  const [foregroundBanner, setForegroundBanner] = useState<ForegroundBanner | null>(null);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);

  useEffect(() => {
    if (!foregroundBanner) {
      return;
    }
    const timeoutId = setTimeout(() => setForegroundBanner(null), FOREGROUND_BANNER_DURATION_MS);
    return () => clearTimeout(timeoutId);
  }, [foregroundBanner]);

  // Registers this device's push token right after a successful login/MFA completion/biometric
  // login (isAuthenticated flips to true) - the app-start effect below only covers a cold start
  // where a session already existed. Fire-and-forget: registerPushToken already handles/logs its
  // own failures (see PushNotificationService.ts) and must never block the auth flow.
  useEffect(() => {
    if (!isAuthenticated) {
      return;
    }
    pushNotificationService.registerPushToken().catch(() => undefined);
  }, [isAuthenticated]);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        // Request notification permissions
        const granted = await pushNotificationService.requestNotificationPermissions();
        if (granted) {
          // Register push token
          await pushNotificationService.registerPushToken();
        }

        // Setup notification listeners. Notification bodies are never logged (PII); the
        // foreground banner only ever displays them to the signed-in user on their own device.
        pushNotificationService.setupNotificationListeners(
          (notification) => {
            const { title, body } = notification.request.content;
            if (title || body) {
              setForegroundBanner({ title: title ?? "", body: body ?? "" });
            }
          },
          (response) => {
            // `data.screen`/`data.type` originate from the push sender: only known tab
            // screens are ever returned, so a compromised sender can't deep-link into
            // arbitrary routes.
            const screen = resolvePushNavigationScreen(response.notification.request.content.data);
            if (screen) {
              pushNavigationTarget = screen;
            }
          }
        );
      } catch {
        // Never log the raw error object (app-wide rule - see AuthService.ts).
        if (__DEV__) {
          console.error("Failed to initialize app.");
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
      {foregroundBanner && (
        <View style={styles.banner} pointerEvents="none">
          {foregroundBanner.title.length > 0 && (
            <Text style={styles.bannerTitle}>{foregroundBanner.title}</Text>
          )}
          {foregroundBanner.body.length > 0 && (
            <Text style={styles.bannerBody}>{foregroundBanner.body}</Text>
          )}
        </View>
      )}
      <RootNavigator />
    </GestureHandlerRootView>
  );
}

const styles = StyleSheet.create({
  banner: {
    position: "absolute",
    top: 48,
    left: 16,
    right: 16,
    zIndex: 100,
    backgroundColor: "#1f2937",
    borderRadius: 12,
    padding: 12,
  },
  bannerTitle: {
    color: "#ffffff",
    fontWeight: "600",
    marginBottom: 2,
  },
  bannerBody: {
    color: "#e5e7eb",
  },
});
