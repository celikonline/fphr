import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Theme = "light" | "dark" | "system";

/**
 * Selectable primary-color palettes. Each key is a corporate color tuple in src/lib/mantine-theme.ts; the
 * Mantine theme swaps `primaryColor` to it so every primary-driven component follows the choice.
 */
export const PALETTES = ["brand", "navy", "blue", "teal", "orange", "gold", "steel"] as const;
export type Palette = (typeof PALETTES)[number];
export const DEFAULT_PALETTE: Palette = "brand";

/** Narrows a persisted/unknown value to a known palette, falling back to the default (e.g. after a rename). */
export function resolvePalette(value: unknown): Palette {
  return (PALETTES as readonly string[]).includes(value as string) ? (value as Palette) : DEFAULT_PALETTE;
}

interface Notification {
  id: string;
  type: "success" | "error" | "info" | "warning";
  message: string;
  duration?: number;
}

interface UIStore {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  palette: Palette;
  setPalette: (palette: Palette) => void;
  notifications: Notification[];
  addNotification: (notification: Omit<Notification, "id">) => void;
  removeNotification: (id: string) => void;
}

export const useUIStore = create<UIStore>()(
  persist(
    (set) => ({
      theme: "light",
      setTheme: (theme) => set({ theme }),
      palette: DEFAULT_PALETTE,
      setPalette: (palette) => set({ palette: resolvePalette(palette) }),
      notifications: [],
      addNotification: (notification) =>
        set((state) => ({
          notifications: [
            ...state.notifications,
            {
              ...notification,
              id: Date.now().toString(),
            },
          ],
        })),
      removeNotification: (id) =>
        set((state) => ({
          notifications: state.notifications.filter((n) => n.id !== id),
        })),
    }),
    {
      name: "ui-store",
      version: 1,
      partialize: (state) => ({ theme: state.theme, palette: state.palette }),
      // "system" was the old default, so migrate that implicit choice to the new light default.
      // Explicit light/dark selections remain untouched; future system selections are persisted normally.
      migrate: (persisted) => {
        const stored = (persisted ?? {}) as Partial<Pick<UIStore, "theme" | "palette">>;
        return {
          theme: stored.theme === "system" || stored.theme == null ? "light" : stored.theme,
          palette: resolvePalette(stored.palette),
        };
      },
      // Older persisted snapshots have no palette (or a since-removed key): normalize instead of trusting storage.
      merge: (persisted, current) => {
        const stored = (persisted ?? {}) as Partial<Pick<UIStore, "theme" | "palette">>;
        return { ...current, ...stored, palette: resolvePalette(stored.palette) };
      },
    }
  )
);
