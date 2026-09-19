import { BrowserRouter as Router, Routes, Route, Navigate } from "react-router";
import { lazy, useEffect } from "react";
import { useAuthStore } from "@/store/auth.store";
import LoginPage from "@/pages/auth/login";
import NotFoundPage from "@/pages/not-found";
import ProtectedRoute from "@/components/protected-route";
import ComingSoonPage from "@/components/coming-soon-page";
import RouteBoundary from "@/components/route-boundary";
import AppLayout from "@/layouts/app-layout";

// Everything below the sign-in screen is code-split: a single bundle made the first paint wait for
// every page in the product. Only the shell (this file, the layout and the login page) is eager;
// each route's chunk is fetched on first visit and cached by filename hash afterwards.
// `RouteBoundary` supplies the Suspense fallback and recovers from a chunk gone stale after a deploy.
const InviteAcceptPage = lazy(() => import("@/pages/auth/invite-accept"));
const ForgotPasswordPage = lazy(() => import("@/pages/auth/forgot-password"));
const ResetPasswordPage = lazy(() => import("@/pages/auth/reset-password"));
const HomePage = lazy(() => import("@/pages/home"));
const CompanyProfilePage = lazy(() => import("@/pages/company-profile"));
const ReportsPage = lazy(() => import("@/pages/reports"));
const LocationsPage = lazy(() => import("@/pages/locations"));
const UsersRolesPage = lazy(() => import("@/pages/users-roles"));
const AuditLogPage = lazy(() => import("@/pages/audit-log"));
const AppsPage = lazy(() => import("@/pages/apps"));
const AccountPage = lazy(() => import("@/pages/account"));
const SupportPage = lazy(() => import("@/pages/support"));
const EmployeeListPage = lazy(() => import("@/pages/employees/employee-list"));
const OrgChartPage = lazy(() => import("@/pages/employees/org-chart"));
const EmployeeCardPage = lazy(() => import("@/pages/employees/employee-card"));
const SettingsHomePage = lazy(() => import("@/pages/settings/settings-home"));
const CompanyStructurePage = lazy(() => import("@/pages/settings/company-structure"));
const HolidayCalendarsPage = lazy(() => import("@/pages/settings/holiday-calendars"));
const AccessSettingsPage = lazy(() => import("@/pages/settings/access-settings"));
const SecurityPolicyPage = lazy(() => import("@/pages/settings/security-policy"));
const SupportAccessPage = lazy(() => import("@/pages/settings/support-access"));
const DataTransferPage = lazy(() => import("@/pages/settings/data-transfer"));
const DataTransferJobPage = lazy(() => import("@/pages/settings/data-transfer-job"));
const ApprovalFlowsPage = lazy(() => import("@/pages/settings/approval-flows"));
const CustomFieldsPage = lazy(() => import("@/pages/settings/custom-fields"));
const TerminationReasonsPage = lazy(() => import("@/pages/settings/termination-reasons"));
const CostCentersPage = lazy(() => import("@/pages/settings/cost-centers"));
const SubcontractorsPage = lazy(() => import("@/pages/settings/subcontractors"));
const DocumentTypesPage = lazy(() => import("@/pages/settings/document-types"));
const MissingDocumentsReportPage = lazy(() => import("@/pages/settings/missing-documents-report"));
const AssetCategoriesPage = lazy(() => import("@/pages/settings/asset-categories"));
const ExpenseCategoriesPage = lazy(() => import("@/pages/settings/expense-categories"));
const OhsHazardClassificationPage = lazy(() => import("@/pages/settings/ohs-hazard-classification"));
const OhsRiskAssessmentsPage = lazy(() => import("@/pages/settings/ohs-risk-assessments"));
const PayrollPeriodsPage = lazy(() => import("@/pages/payroll/periods"));
const PayrollPeriodDetailPage = lazy(() => import("@/pages/payroll/period-detail"));
const WorkflowInboxPage = lazy(() => import("@/pages/inbox"));
const NotificationPreferencesPage = lazy(() => import("@/pages/notifications/preferences"));
const ApproveTokenPage = lazy(() => import("@/pages/public/approve"));
const CareerJobPostingPage = lazy(() => import("@/pages/public/career/job-posting"));
const CareerPostingsListPage = lazy(() => import("@/pages/public/career/postings"));
const RequestsPage = lazy(() => import("@/pages/requests"));
const MyLeavePage = lazy(() => import("@/pages/leave/my-leave"));
const LeaveRuleSetsPage = lazy(() => import("@/pages/settings/leave-rule-sets"));
const LeaveCalendarPage = lazy(() => import("@/pages/leave/leave-calendar"));
const MyOvertimesPage = lazy(() => import("@/pages/leave/my-overtimes"));
const CompanyLeaveLedgerPage = lazy(() => import("@/pages/leave/company-leave-ledger"));
const JobPostingsPage = lazy(() => import("@/pages/recruitment/job-postings"));
const JobPostingDetailPage = lazy(() => import("@/pages/recruitment/job-posting-detail"));
const CandidatesPage = lazy(() => import("@/pages/recruitment/candidates"));
const TrustPage = lazy(() => import("@/pages/trust"));
const JobRequisitionsPage = lazy(() => import("@/pages/recruitment/job-requisitions"));
const RecruitmentFormTemplatesPage = lazy(() => import("@/pages/settings/recruitment-form-templates"));
const RecruitmentAutomationRulesPage = lazy(() => import("@/pages/settings/recruitment-automation-rules"));
const PaymentsPage = lazy(() => import("@/pages/payments"));
const ExpensePaymentsAdminPage = lazy(() => import("@/pages/expense-payments-admin"));
const MyTimesheetPage = lazy(() => import("@/pages/attendance/my-timesheet"));
const MyShiftsPage = lazy(() => import("@/pages/attendance/my-shifts"));
const TeamTimesheetPage = lazy(() => import("@/pages/attendance/team-timesheet"));
const DeviceLogImportPage = lazy(() => import("@/pages/attendance/device-log-import"));
const ShiftPlanPage = lazy(() => import("@/pages/attendance/shift-plan"));
const WorkSchedulesPage = lazy(() => import("@/pages/settings/work-schedules"));
const MyGoalsPage = lazy(() => import("@/pages/performance/my-goals"));
const TeamGoalsPage = lazy(() => import("@/pages/performance/team-goals"));
const ReviewCyclesPage = lazy(() => import("@/pages/performance/review-cycles"));
const CompanyCalendarPage = lazy(() => import("@/pages/company-calendar"));
const SharedFilesPage = lazy(() => import("@/pages/shared-files"));

const STRUCTURE_PATH = "/app/settings/company/structure";

export default function App() {
  const hasToken = useAuthStore((state) => !!state.token);
  const refreshMe = useAuthStore((state) => state.refreshMe);

  // Persisted permissions/access settings may be stale (e.g. changed by an admin since last visit).
  useEffect(() => {
    if (hasToken) {
      void refreshMe();
    }
  }, [hasToken, refreshMe]);

  return (
    <Router>
      {/* Outer boundary covers the public pages; AppLayout has its own so the shell survives a page swap. */}
      <RouteBoundary>
        <Routes>
          {/* Public Routes */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="/invite/:token" element={<InviteAcceptPage />} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/reset-password/:token" element={<ResetPasswordPage />} />
          <Route path="/approve/:token" element={<ApproveTokenPage />} />
          <Route path="/kariyer" element={<CareerPostingsListPage />} />
          <Route path="/kariyer/:applySlug" element={<CareerJobPostingPage />} />

          {/* Protected Routes: single app shell (sidebar + header) */}
          <Route
            path="/app"
            element={
              <ProtectedRoute>
                <AppLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<HomePage />} />

            <Route path="employees" element={<EmployeeListPage />} />
            <Route path="employees/org-chart" element={<OrgChartPage />} />
            <Route path="employees/:id/:tab?" element={<EmployeeCardPage />} />

            <Route path="leaves" element={<Navigate to="/app/leave" replace />} />
            <Route path="calendar" element={<Navigate to="/app/leave/calendar" replace />} />
            <Route path="requests" element={<RequestsPage />} />
            <Route path="shared-files" element={<SharedFilesPage />} />
            <Route path="reports" element={<ReportsPage />} />
            <Route path="support" element={<SupportPage />} />
            <Route path="apps" element={<AppsPage />} />
            <Route path="account" element={<AccountPage />} />
            <Route path="trust" element={<TrustPage />} />
            <Route path="inbox" element={<WorkflowInboxPage />} />
            <Route path="notifications/settings" element={<NotificationPreferencesPage />} />
            <Route path="leave" element={<MyLeavePage />} />
            <Route path="leave/calendar" element={<LeaveCalendarPage />} />
            <Route path="leave/overtimes" element={<MyOvertimesPage />} />
            <Route path="company-calendar" element={<CompanyCalendarPage />} />
            <Route path="leave/company-ledger" element={<CompanyLeaveLedgerPage />} />
            <Route path="payments" element={<PaymentsPage />} />
            <Route path="expenses/payments" element={<ExpensePaymentsAdminPage />} />
            <Route path="payroll/periods" element={<PayrollPeriodsPage />} />
            <Route path="payroll/periods/:id" element={<PayrollPeriodDetailPage />} />
            <Route path="attendance/timesheet" element={<MyTimesheetPage />} />
            <Route path="attendance/shifts" element={<MyShiftsPage />} />
            <Route path="attendance/team-timesheet" element={<TeamTimesheetPage />} />
            <Route path="attendance/device-logs/import" element={<DeviceLogImportPage />} />
            <Route path="attendance/shift-plan" element={<ShiftPlanPage />} />

            <Route path="performance/goals" element={<MyGoalsPage />} />
            <Route path="performance/team-goals" element={<TeamGoalsPage />} />
            <Route path="performance/review-cycles" element={<ReviewCyclesPage />} />

            <Route path="recruitment/postings" element={<JobPostingsPage />} />
            <Route path="recruitment/postings/:id" element={<JobPostingDetailPage />} />
            <Route path="recruitment/candidates" element={<CandidatesPage />} />
            <Route path="recruitment/requisitions" element={<JobRequisitionsPage />} />
            <Route path="settings/recruitment/form-templates" element={<RecruitmentFormTemplatesPage />} />
            <Route path="settings/recruitment/automation-rules" element={<RecruitmentAutomationRulesPage />} />

            <Route path="settings" element={<SettingsHomePage />} />
            <Route path="settings/company/structure" element={<CompanyStructurePage />} />
            <Route path="settings/company/holiday-calendars" element={<HolidayCalendarsPage />} />
            <Route path="settings/company/locations" element={<LocationsPage />} />
            <Route path="settings/company/profile" element={<CompanyProfilePage />} />
            <Route path="settings/company/approval-flows" element={<ApprovalFlowsPage />} />
            <Route path="settings/company/custom-fields" element={<CustomFieldsPage />} />
            <Route path="settings/company/termination-reasons" element={<TerminationReasonsPage />} />
            <Route path="settings/company/cost-centers" element={<CostCentersPage />} />
            <Route path="settings/company/subcontractors" element={<SubcontractorsPage />} />
            <Route path="settings/company/document-types" element={<DocumentTypesPage />} />
            <Route path="settings/company/missing-documents" element={<MissingDocumentsReportPage />} />
            <Route path="settings/company/asset-categories" element={<AssetCategoriesPage />} />
            <Route path="settings/company/expense-categories" element={<ExpenseCategoriesPage />} />
            <Route path="settings/company/ohs-hazard-classification" element={<OhsHazardClassificationPage />} />
            <Route path="settings/company/ohs-risk-assessments" element={<OhsRiskAssessmentsPage />} />
            <Route path="settings/rules/work-calendars" element={<WorkSchedulesPage />} />
            <Route
              path="settings/rules/*"
              element={
                <ComingSoonPage
                  titleKey="settings.rules"
                  phase={1}
                  itemKeys={["settings.visaProcesses"]}
                />
              }
            />
            <Route path="settings/system/access" element={<AccessSettingsPage />} />
            <Route path="settings/system/security-policy" element={<SecurityPolicyPage />} />
            <Route path="settings/system/users-roles" element={<UsersRolesPage />} />
            <Route path="settings/system/audit-log" element={<AuditLogPage />} />
            <Route path="settings/system/support-access" element={<SupportAccessPage />} />
            <Route path="settings/system/billing" element={<ComingSoonPage titleKey="settings.billing" phase={8} />} />
            <Route path="settings/data-transfer" element={<DataTransferPage />} />
            <Route path="settings/data-transfer/jobs/:id" element={<DataTransferJobPage />} />
            <Route path="settings/leave/rule-sets" element={<LeaveRuleSetsPage />} />

            {/* Old paths (pre settings IA) */}
            <Route path="organization" element={<Navigate to={STRUCTURE_PATH} replace />} />
            <Route path="positions" element={<Navigate to={STRUCTURE_PATH} replace />} />
            <Route path="locations" element={<Navigate to="/app/settings/company/locations" replace />} />
            <Route path="company-profile" element={<Navigate to="/app/settings/company/profile" replace />} />
            <Route path="users-roles" element={<Navigate to="/app/settings/system/users-roles" replace />} />
            <Route path="audit-log" element={<Navigate to="/app/settings/system/audit-log" replace />} />
          </Route>

          {/* Default redirect */}
          <Route path="/" element={<Navigate to="/app" replace />} />

          {/* 404 - Not Found */}
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </RouteBoundary>
    </Router>
  );
}
