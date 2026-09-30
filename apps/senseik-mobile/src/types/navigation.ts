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

/** The request types the tab bar's "Yeni talep" sheet can start. */
export type RequestKind = "leave" | "overtime" | "expense" | "advance" | "punchCorrection";

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
  /** `date` preselects the day (FG-004: Puantaj's "Düzeltme iste" opens punchCorrection with it set). */
  NewRequest: { kind: RequestKind; date?: string };
  ApprovalHistory: undefined;
  RequestDetail: { approvalRequestId: string; summary?: RequestDetailSummary };
  PunchRequest: undefined;
  PayslipDetail: { payslip: import("@/services/PayslipService").MyPayslip };
};

/**
 * FG-009: passed to `RequestDetail` from a Taleplerim/Onaylarım card so the header shows type/dates/
 * amount instantly, without waiting on `GetApprovalStatusQuery` (which carries no type-specific
 * summary - see the FG-009 backend/P06 report). Absent when opened from a notification deep link;
 * the screen then falls back to a generic request-type label once the status call resolves.
 */
export type RequestDetailSummary = {
  title: string;
  dateLabel: string;
  detail: string;
  description: string | null;
};

export type MainTabsParamList = {
  Home: undefined;
  CheckIn: undefined;
};
