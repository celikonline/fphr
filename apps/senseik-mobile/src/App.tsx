import React, { useEffect } from "react";
import { StatusBar } from "expo-status-bar";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { RootNavigator } from "@/navigation/RootNavigator";
import { pushNotificationService } from "@/services/PushNotificationService";
import { SecureStorage } from "@/lib/storage";

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

        // Setup notification listeners
        pushNotificationService.setupNotificationListeners(
          notification => {
            console.log("Notification received:", notification);
          },
          response => {
            const screen = response.notification.request.content.data?.screen;
            if (screen) {
              // Navigation will be handled by the app store or screen
              console.log("Navigate to:", screen);
            }
          }
        );
      } catch (error) {
        console.error("Failed to initialize app:", error);
      }
    };

    initializeApp();

    return () => {
      pushNotificationService.removeNotificationListeners();
    };
  }, []);

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <StatusBar barStyle="light-content" />
      <RootNavigator />
    </GestureHandlerRootView>
  );
}
