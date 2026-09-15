import { BrowserRouter as Router, Routes, Route, Navigate } from "react-router";
import { useEffect } from "react";
import { useAuthStore } from "@/store/auth.store";
import LoginPage from "@/pages/auth/login";
import InviteAcceptPage from "@/pages/auth/invite-accept";
import HomePage from "@/pages/home";
import NotFoundPage from "@/pages/not-found";
import ProtectedRoute from "@/components/protected-route";
import ComingSoonPage from "@/components/coming-soon-page";
import AppLayout from "@/layouts/app-layout";
import CompanyProfilePage from "@/pages/company-profile";
import LocationsPage from "@/pages/locations";
import UsersRolesPage from "@/pages/users-roles";
import AuditLogPage from "@/pages/audit-log";
import AppsPage from "@/pages/apps";
import AccountPage from "@/pages/account";
import EmployeeListPage from "@/pages/employees/employee-list";
import OrgChartPage from "@/pages/employees/org-chart";
import EmployeeCardPage from "@/pages/employees/employee-card";
import SettingsHomePage from "@/pages/settings/settings-home";
import CompanyStructurePage from "@/pages/settings/company-structure";
import HolidayCalendarsPage from "@/pages/settings/holiday-calendars";
import AccessSettingsPage from "@/pages/settings/access-settings";
import SupportAccessPage from "@/pages/settings/support-access";
import DataTransferPage from "@/pages/settings/data-transfer";
import DataTransferJobPage from "@/pages/settings/data-transfer-job";
import ApprovalFlowsPage from "@/pages/settings/approval-flows";
import CustomFieldsPage from "@/pages/settings/custom-fields";
import WorkflowInboxPage from "@/pages/inbox";
import NotificationPreferencesPage from "@/pages/notifications/preferences";
import ApproveTokenPage from "@/pages/public/approve";
import MyLeavePage from "@/pages/leave/my-leave";
import LeaveRuleSetsPage from "@/pages/settings/leave-rule-sets";
import LeaveCalendarPage from "@/pages/leave/leave-calendar";
import MyOvertimesPage from "@/pages/leave/my-overtimes";
import JobPostingsPage from "@/pages/recruitment/job-postings";
import JobPostingDetailPage from "@/pages/recruitment/job-posting-detail";
import CandidatesPage from "@/pages/recruitment/candidates";

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
      <Routes>
        {/* Public Routes */}
        <Route path="/login" element={<LoginPage />} />
        <Route path="/invite/:token" element={<InviteAcceptPage />} />
        <Route path="/approve/:token" element={<ApproveTokenPage />} />

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
          <Route path="requests" element={<ComingSoonPage titleKey="nav.requests" phase={1} />} />
          <Route path="reports" element={<ComingSoonPage titleKey="nav.reports" phase={4} />} />
          <Route path="support" element={<ComingSoonPage titleKey="nav.support" phase={4} />} />
          <Route path="apps" element={<AppsPage />} />
          <Route path="account" element={<AccountPage />} />
          <Route path="inbox" element={<WorkflowInboxPage />} />
          <Route path="notifications/settings" element={<NotificationPreferencesPage />} />
          <Route path="leave" element={<MyLeavePage />} />
          <Route path="leave/calendar" element={<LeaveCalendarPage />} />
          <Route path="leave/overtimes" element={<MyOvertimesPage />} />

          <Route path="recruitment/postings" element={<JobPostingsPage />} />
          <Route path="recruitment/postings/:id" element={<JobPostingDetailPage />} />
          <Route path="recruitment/candidates" element={<CandidatesPage />} />

          <Route path="settings" element={<SettingsHomePage />} />
          <Route path="settings/company/structure" element={<CompanyStructurePage />} />
          <Route path="settings/company/holiday-calendars" element={<HolidayCalendarsPage />} />
          <Route path="settings/company/locations" element={<LocationsPage />} />
          <Route path="settings/company/profile" element={<CompanyProfilePage />} />
          <Route path="settings/company/approval-flows" element={<ApprovalFlowsPage />} />
          <Route path="settings/company/custom-fields" element={<CustomFieldsPage />} />
          <Route
            path="settings/rules/*"
            element={
              <ComingSoonPage
                titleKey="settings.rules"
                phase={1}
                itemKeys={["settings.workCalendars", "settings.visaProcesses"]}
              />
            }
          />
          <Route path="settings/system/access" element={<AccessSettingsPage />} />
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
    </Router>
  );
}
