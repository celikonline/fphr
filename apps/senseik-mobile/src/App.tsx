import React, { useEffect, useState } from "react";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { useFonts } from "expo-font";
import * as SplashScreen from "expo-splash-screen";
import {
  Manrope_400Regular,
  Manrope_500Medium,
  Manrope_600SemiBold,
  Manrope_700Bold,
  Manrope_800ExtraBold,
} from "@expo-google-fonts/manrope";
import { RootNavigator } from "@/navigation/RootNavigator";
import { pushNotificationService } from "@/services/PushNotificationService";
import { resolvePushNavigationScreen } from "@/lib/pushNavigation";
import type { NavigationTarget } from "@/lib/notificationNavigation";
import { useAuthStore } from "@/store/authStore";
import { PushBanner } from "@/components/ui/PushBanner";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { loadInitialLanguage } from "@/lib/i18n";

/** How long the foreground in-app banner stays visible before auto-dismissing. */
const FOREGROUND_BANNER_DURATION_MS = 4000;

// Keeps the native splash screen up while Manrope loads instead of flashing the system font -
// swallow a repeat call (e.g. Fast Refresh) rather than let it reject and go unhandled.
SplashScreen.preventAutoHideAsync().catch(() => undefined);

/**
 * Last validated push-navigation request. RootNavigator consumes and clears it after
 * authentication is confirmed, so an unauthenticated cold start can't land inside the app.
 */
export let pushNavigationTarget: NavigationTarget | null = null;
export function consumePushNavigationTarget(): NavigationTarget | null {
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
  const [fontsLoaded, fontError] = useFonts({
    Manrope_400Regular,
    Manrope_500Medium,
    Manrope_600SemiBold,
    Manrope_700Bold,
    Manrope_800ExtraBold,
  });
  // A font load failure falls back to the system font rather than blocking the app - only the
  // still-pending (neither loaded nor errored) case keeps the splash screen up.
  const fontsReady = fontsLoaded || Boolean(fontError);
  // FG-001: the saved language (or the device language) is applied before the splash screen goes
  // away, so the first frame is never rendered in the wrong language. A storage failure still
  // lets the app start (in the device/default language).
  const [languageReady, setLanguageReady] = useState(false);
  const ready = fontsReady && languageReady;

  useEffect(() => {
    loadInitialLanguage()
      .catch(() => undefined)
      .finally(() => setLanguageReady(true));
  }, []);

  useEffect(() => {
    if (ready) {
      SplashScreen.hideAsync().catch(() => undefined);
    }
  }, [ready]);

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
            // `data.screen`/`data.type` originate from the push sender: only known
            // navigation targets are ever returned, so a compromised sender can't deep-link
            // into arbitrary routes.
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

  if (!ready) {
    // Native splash screen (kept up by preventAutoHideAsync above) is still covering the screen.
    return null;
  }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      {foregroundBanner && (
        <PushBanner title={foregroundBanner.title} body={foregroundBanner.body} />
      )}
      <ErrorBoundary>
        <RootNavigator />
      </ErrorBoundary>
    </GestureHandlerRootView>
  );
}
