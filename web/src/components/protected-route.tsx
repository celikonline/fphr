import { ReactNode } from "react";
import { Navigate } from "react-router";
import { Center, Loader } from "@mantine/core";
import { useAuthStore } from "@/store/auth.store";

interface ProtectedRouteProps {
  children: ReactNode;
}

/**
 * Gates on `hasHydrated` before checking `isAuthenticated()`. `user`/`features` (permissions,
 * access settings) only exist in the store after zustand's `persist` middleware restores them
 * from storage, which happens a tick after the store is created - not before the first render.
 * Rendering the shell before that point would show every permission-gated button/menu item as
 * absent (an authenticated user briefly looks unauthorized, or a real logged-out user briefly
 * flashes the app shell before the redirect) until the store catches up (senseik-042).
 */
export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const hasHydrated = useAuthStore((state) => state.hasHydrated);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated());

  if (!hasHydrated) {
    return (
      <Center h="100vh" data-testid="protected-route-loading">
        <Loader />
      </Center>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}
