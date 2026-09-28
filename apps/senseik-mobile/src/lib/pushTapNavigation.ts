import type { NavigationContainerRef } from "@react-navigation/native";
import type { NotificationResponse } from "expo-notifications";
import type {
  NavigationTarget,
  StackTargetScreen,
  TargetNavigator,
} from "@/lib/notificationNavigation";
import { resolvePushNavigationScreen } from "@/lib/pushNavigation";
import type { MainTabsParamList, RootStackParamList } from "@/types/navigation";

/**
 * Where a tapped push notification should open, or `null` when its payload names no known target.
 * Only the allow-listed names in `data` are read (see `resolvePushNavigationScreen`) - never a URL.
 */
export function resolvePushTapTarget(response: NotificationResponse): NavigationTarget | null {
  return resolvePushNavigationScreen(response.notification.request.content.data);
}

/**
 * Adapts the root container ref to the AppStack-level navigator `navigateToTarget` expects. Every
 * AppStack screen sits under the root "App" route, and react-navigation 7 no longer resolves a
 * nested screen by name from a parent navigator, so each call is forwarded as a nested navigate.
 */
export function appStackNavigation(
  root: Pick<NavigationContainerRef<RootStackParamList>, "navigate">
): TargetNavigator {
  return {
    navigate(screen: StackTargetScreen | "MainTabs", params?: { screen: keyof MainTabsParamList }) {
      if (params) {
        root.navigate("App", { screen: "MainTabs", params });
        // `TargetNavigator` never calls "MainTabs" without params, so this guard only narrows the type.
      } else if (screen !== "MainTabs") {
        root.navigate("App", { screen });
      }
    },
  };
}
