import { useAuthStore } from "@/store/authStore";
import { useMemo } from "react";

export const useAuth = () => {
  const {
    isAuthenticated,
    isLoading,
    error,
    user,
    tenantId,
    tokens,
    login,
    loginWithBiometric,
    logout,
    refreshToken,
    clearError
  } = useAuthStore();

  return useMemo(
    () => ({
      isAuthenticated,
      isLoading,
      error,
      user,
      tenantId,
      tokens,
      login,
      loginWithBiometric,
      logout,
      refreshToken,
      clearError
    }),
    [isAuthenticated, isLoading, error, user, tenantId, tokens]
  );
};
