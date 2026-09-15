import { BrowserRouter as Router, Routes, Route, Navigate } from "react-router-dom";
import { useEffect } from "react";
import { useUIStore } from "@/store/ui.store";
import LoginPage from "@/pages/auth/login";
import DashboardPage from "@/pages/dashboard";
import NotFoundPage from "@/pages/not-found";
import ProtectedRoute from "@/components/protected-route";

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

        {/* Protected Routes */}
        <Route
          path="/app"
          element={
            <ProtectedRoute>
              <div className="flex h-screen">
                {/* Sidebar will go here - Item 20 */}
                {/* Main content area */}
              </div>
            </ProtectedRoute>
          }
        >
          {/* Item 20 routes will be added here */}
        </Route>

        {/* Dashboard / Home */}
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <DashboardPage />
            </ProtectedRoute>
          }
        />

        {/* Default redirect */}
        <Route path="/" element={<Navigate to="/app" replace />} />

        {/* 404 - Not Found */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Router>
  );
}
