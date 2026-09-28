import { useEffect, useRef, useState } from "react";
import * as Notifications from "expo-notifications";
import type { NavigationContainerRef } from "@react-navigation/native";
import { navigateToTarget, type NavigationTarget } from "@/lib/notificationNavigation";
import { appStackNavigation, resolvePushTapTarget } from "@/lib/pushTapNavigation";
import type { RootStackParamList } from "@/types/navigation";

/**
 * Opens the screen a tapped push notification points at. Taps are captured from app start - the one
 * that cold-launched the app via `getLastNotificationResponse`, later ones via the response listener
 * - but only acted on once `canNavigate` (signed in and the container ready), so a tap made while
 * signed out waits for login instead of landing inside the app.
 */
export function usePushTapNavigation(
  root: Pick<NavigationContainerRef<RootStackParamList>, "navigate">,
  canNavigate: boolean
): void {
  const [pendingTarget, setPendingTarget] = useState<NavigationTarget | null>(null);
  const handledResponseId = useRef<string | null>(null);

  useEffect(() => {
    const handleResponse = (response: Notifications.NotificationResponse) => {
      // On a cold start the launching tap can arrive both ways - act on it once.
      const responseId = response.notification.request.identifier;
      if (responseId === handledResponseId.current) {
        return;
      }
      handledResponseId.current = responseId;
      // Handled: a later JS reload in the same process must not replay it.
      Notifications.clearLastNotificationResponse();

      const target = resolvePushTapTarget(response);
      if (target) {
        setPendingTarget(target);
      }
    };

    const lastResponse = Notifications.getLastNotificationResponse();
    if (lastResponse) {
      handleResponse(lastResponse);
    }
    const subscription = Notifications.addNotificationResponseReceivedListener(handleResponse);
    return () => subscription.remove();
  }, []);

  useEffect(() => {
    if (!canNavigate || !pendingTarget) {
      return;
    }
    navigateToTarget(appStackNavigation(root), pendingTarget);
    setPendingTarget(null);
  }, [canNavigate, pendingTarget, root]);
}
