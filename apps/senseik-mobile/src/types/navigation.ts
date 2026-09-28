import { NavigatorScreenParams } from "@react-navigation/native";

export type RootStackParamList = {
  Splash: undefined;
  Auth: NavigatorScreenParams<AuthStackParamList>;
  App: NavigatorScreenParams<AppStackParamList>;
};

export type AuthStackParamList = {
  Login: undefined;
  BiometricPrompt: { email: string };
  ForgotPassword: undefined;
};

/** The four request types the tab bar's "Yeni talep" sheet can start. */
export type RequestKind = "leave" | "overtime" | "expense" | "advance";

export type AppStackParamList = {
  MainTabs: NavigatorScreenParams<MainTabsParamList>;
  Attendance: undefined;
  Leave: undefined;
  Payroll: undefined;
  Approvals: undefined;
  Profile: undefined;
  Requests: undefined;
  People: undefined;
  Shift: undefined;
  Goals: undefined;
  Support: undefined;
  Notifications: undefined;
  NotificationPreferences: undefined;
  NewRequest: { kind: RequestKind };
};

export type MainTabsParamList = {
  Home: undefined;
  CheckIn: undefined;
};
