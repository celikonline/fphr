import { create } from "zustand";
import { persist } from "zustand/middleware";

export interface TenantMetadata {
  id: string;
  name: string;
  logo?: string;
  theme?: {
    primaryColor?: string;
    accentColor?: string;
  };
}

interface TenantStore {
  currentTenant: TenantMetadata | null;
  setCurrentTenant: (tenant: TenantMetadata) => void;
  clearCurrentTenant: () => void;
}

export const useTenantStore = create<TenantStore>()(
  persist(
    (set) => ({
      currentTenant: null,
      setCurrentTenant: (tenant) => set({ currentTenant: tenant }),
      clearCurrentTenant: () => set({ currentTenant: null }),
    }),
    {
      name: "tenant-store",
    }
  )
);
