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

export type AppStackParamList = {
  MainTabs: NavigatorScreenParams<MainTabsParamList>;
  Settings: undefined;
};

export type MainTabsParamList = {
  Dashboard: undefined;
  Attendance: undefined;
  Leave: undefined;
  Payroll: undefined;
  Approvals: undefined;
  Profile: undefined;
};
