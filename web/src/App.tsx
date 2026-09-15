import { BrowserRouter as Router, Routes, Route, Navigate } from "react-router-dom";
import { useEffect } from "react";
import { useUIStore } from "@/store/ui.store";
import LoginPage from "@/pages/auth/login";
import DashboardPage from "@/pages/dashboard";
import NotFoundPage from "@/pages/not-found";
import ProtectedRoute from "@/components/protected-route";
import AppLayout from "@/layouts/app-layout";
import CompanyProfilePage from "@/pages/company-profile";
import OrganizationPage from "@/pages/organization";
import LocationsPage from "@/pages/locations";
import PositionsPage from "@/pages/positions";
import UsersRolesPage from "@/pages/users-roles";
import AuditLogPage from "@/pages/audit-log";

export default function App() {
  const theme = useUIStore((state) => state.theme);

  useEffect(() => {
    const isDark = theme === "dark" || (theme === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
    if (isDark) {
      document.documentElement.classList.add("dark");
    } else {
      document.documentElement.classList.remove("dark");
    }
  }, [theme]);

  return (
    <Router>
      <Routes>
        {/* Public Routes */}
        <Route path="/login" element={<LoginPage />} />

        {/* Protected Routes: app shell (sidebar + header) with nested Item 20 screens */}
        <Route
          path="/app"
          element={
            <ProtectedRoute>
              <AppLayout />
            </ProtectedRoute>
          }
        >
          <Route index element={<DashboardPage />} />
          <Route path="company-profile" element={<CompanyProfilePage />} />
          <Route path="organization" element={<OrganizationPage />} />
          <Route path="locations" element={<LocationsPage />} />
          <Route path="positions" element={<PositionsPage />} />
          <Route path="users-roles" element={<UsersRolesPage />} />
          <Route path="audit-log" element={<AuditLogPage />} />
        </Route>

        {/* Default redirect */}
        <Route path="/" element={<Navigate to="/app" replace />} />

        {/* 404 - Not Found */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Router>
  );
}
