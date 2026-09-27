import { createNavigationContainerRef } from "@react-navigation/native";
import type { RootStackParamList } from "@/types/navigation";

/** Root `NavigationContainer` ref, for navigating from outside a screen (push notification taps). */
export const navigationRef = createNavigationContainerRef<RootStackParamList>();
