import React, { useEffect, useState } from "react";
import { NavigationContainer } from "@react-navigation/native";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { createBottomTabNavigator } from "@react-navigation/bottom-tabs";
import { ActivityIndicator, AppState, StyleSheet, View } from "react-native";
import { StatusBar } from "expo-status-bar";
import { useAuthStore } from "@/store/authStore";
import { palette } from "@/theme/theme";
import { NotchedTabBar } from "@/navigation/NotchedTabBar";
import { resolveStatusBarStyle } from "@/navigation/statusBarStyle";
import {
  RootStackParamList,
  AuthStackParamList,
  AppStackParamList,
  MainTabsParamList,
} from "@/types/navigation";

// Screens (will be created)
import LoginScreen from "@/screens/auth/LoginScreen";
import BiometricPromptScreen from "@/screens/auth/BiometricPromptScreen";
import ForgotPasswordScreen from "@/screens/auth/ForgotPasswordScreen";
import DashboardScreen from "@/screens/app/DashboardScreen";
import AttendanceScreen from "@/screens/app/AttendanceScreen";
import LeaveScreen from "@/screens/app/LeaveScreen";
import PayrollScreen from "@/screens/app/PayrollScreen";
import ApprovalsScreen from "@/screens/app/ApprovalsScreen";
import ProfileScreen from "@/screens/app/ProfileScreen";
import SupportScreen from "@/screens/app/SupportScreen";
import CheckInScreen from "@/screens/app/CheckInScreen";
import PeopleScreen from "@/screens/app/PeopleScreen";
import ShiftScreen from "@/screens/app/ShiftScreen";
import GoalsScreen from "@/screens/app/GoalsScreen";
import NotificationsScreen from "@/screens/app/NotificationsScreen";
import NotificationPreferencesScreen from "@/screens/app/NotificationPreferencesScreen";
import RequestsScreen from "@/screens/app/RequestsScreen";
import NewRequestScreen from "@/screens/app/NewRequestScreen";

const RootStack = createNativeStackNavigator<RootStackParamList>();
const AuthStack = createNativeStackNavigator<AuthStackParamList>();
const AppStack = createNativeStackNavigator<AppStackParamList>();
const Tab = createBottomTabNavigator<MainTabsParamList>();

/**
 * Whether the root spinner (full-screen, replaces the whole navigator) should render instead of the
 * Auth/App navigator stack. Gated ONLY on the initial `checkAuth()` finishing (`appReady`) - never on
 * `isLoading` from later store actions (`login`, `completeMfa`, `logout`, `refreshToken`,
 * `loginWithBiometric`). Those set `isLoading` on the SAME screen the user is already looking at;
 * gating this splash on `isLoading` used to swap the whole navigator out for the spinner the instant
 * `login`/`completeMfa` started, unmounting `LoginScreen` and wiping its local state (typed
 * email/password/MFA code, the inline error banner) before the request even resolved - B14/B15.
 * Those actions show their own inline spinner instead (`LoginScreen`'s submit button).
 * Exported as a pure function so the fix is unit-testable without mounting the full navigator.
 */
export function shouldShowRootSplash(appReady: boolean): boolean {
  return !appReady;
}

export { resolveStatusBarStyle };

/**
 * B18: the app was observed reverting to the wrong status bar style after returning from the
 * background. `expo-status-bar`'s declarative
 * `<StatusBar style={...}>` only re-applies the native style when its own prop changes - it never
 * changes here across a background/foreground cycle, so nothing re-asserts it if anything else
 * (the OS, or a native dialog window opened earlier) altered the status bar appearance while
 * backgrounded. Re-apply it imperatively whenever the app becomes active again.
 */
export function shouldReapplyStatusBarOnAppStateChange(nextAppState: string): boolean {
  return nextAppState === "active";
}

function AuthNavigator() {
  return (
    <AuthStack.Navigator
      screenOptions={{
        headerShown: false,
      }}
    >
      <AuthStack.Screen name="Login" component={LoginScreen} />
      <AuthStack.Screen name="BiometricPrompt" component={BiometricPromptScreen} />
      <AuthStack.Screen name="ForgotPassword" component={ForgotPasswordScreen} />
    </AuthStack.Navigator>
  );
}

function MainTabsNavigator() {
  return (
    <Tab.Navigator
      tabBar={(props) => <NotchedTabBar {...props} />}
      screenOptions={{
        headerShown: false,
        // The custom tab bar overlays the scene (its notch corners are transparent), so the scene
        // behind it must be the dark backdrop, not the default light theme background.
        sceneStyle: { backgroundColor: palette.night },
      }}
    >
      <Tab.Screen name="Home" component={DashboardScreen} options={{ title: "Ana sayfa" }} />
      <Tab.Screen name="CheckIn" component={CheckInScreen} options={{ title: "Giriş / Çıkış" }} />
    </Tab.Navigator>
  );
}

function AppNavigator() {
  return (
    // Every stack screen draws its own header (`ScreenHeader`) - see spec §3.
    <AppStack.Navigator screenOptions={{ headerShown: false }}>
      <AppStack.Screen name="MainTabs" component={MainTabsNavigator} />
      <AppStack.Screen name="Attendance" component={AttendanceScreen} />
      <AppStack.Screen name="Leave" component={LeaveScreen} />
      <AppStack.Screen name="Payroll" component={PayrollScreen} />
      <AppStack.Screen name="Approvals" component={ApprovalsScreen} />
      <AppStack.Screen name="Profile" component={ProfileScreen} />
      <AppStack.Screen name="Requests" component={RequestsScreen} />
      <AppStack.Screen name="People" component={PeopleScreen} />
      <AppStack.Screen name="Shift" component={ShiftScreen} />
      <AppStack.Screen name="Goals" component={GoalsScreen} />
      <AppStack.Screen name="Support" component={SupportScreen} />
      <AppStack.Screen name="Notifications" component={NotificationsScreen} />
      <AppStack.Screen name="NotificationPreferences" component={NotificationPreferencesScreen} />
      <AppStack.Screen name="NewRequest" component={NewRequestScreen} />
    </AppStack.Navigator>
  );
}

export function RootNavigator() {
  const { isAuthenticated, checkAuth } = useAuthStore();
  const [appReady, setAppReady] = useState(false);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        await checkAuth();
      } catch {
        // Never log the error object here either - `checkAuth` already stores it in `error` state.
        if (__DEV__) {
          console.error("Failed to check authentication.");
        }
      } finally {
        setAppReady(true);
      }
    };

    initializeApp();
  }, [checkAuth]);

  const statusBarStyle = resolveStatusBarStyle();

  // B18: see `shouldReapplyStatusBarOnAppStateChange` - re-assert the current style whenever the app
  // returns to the foreground, since nothing else in this component's own props changes across a
  // background/foreground cycle to trigger `<StatusBar>` to re-apply it natively.
  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextAppState) => {
      if (shouldReapplyStatusBarOnAppStateChange(nextAppState)) {
        StatusBar.setStyle(statusBarStyle);
      }
    });
    return () => subscription.remove();
  }, [statusBarStyle]);

  if (shouldShowRootSplash(appReady)) {
    return (
      <>
        <StatusBar style={statusBarStyle} />
        <View style={styles.splash}>
          <ActivityIndicator size="large" color={palette.brand} />
        </View>
      </>
    );
  }

  return (
    <>
      <StatusBar style={statusBarStyle} />
      <NavigationContainer>
        <RootStack.Navigator
          screenOptions={{
            headerShown: false,
          }}
        >
          {!isAuthenticated ? (
            <RootStack.Screen
              name="Auth"
              component={AuthNavigator}
              options={{
                animation: "none",
              }}
            />
          ) : (
            <RootStack.Screen
              name="App"
              component={AppNavigator}
              options={{
                animation: "none",
              }}
            />
          )}
        </RootStack.Navigator>
      </NavigationContainer>
    </>
  );
}

const styles = StyleSheet.create({
  splash: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: palette.night,
  },
});
