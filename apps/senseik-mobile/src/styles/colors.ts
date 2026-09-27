import { palette } from "@/theme/theme";

export const colors = {
  // Primary
  primary: palette.brand,
  primaryLight: "#4d94ff",
  primaryDark: "#0052a3",

  // Secondary
  secondary: "#34c759",
  secondaryLight: "#5dde76",
  secondaryDark: "#2ba546",

  // Neutral
  white: "#ffffff",
  black: "#000000",
  gray50: "#f9fafb",
  gray100: "#f3f4f6",
  gray200: "#e5e7eb",
  gray300: "#d1d5db",
  gray400: "#9ca3af",
  gray500: "#6b7280",
  gray600: "#4b5563",
  gray700: "#374151",
  gray800: "#1f2937",
  gray900: "#111827",

  // Status
  success: "#34c759",
  warning: "#ff9500",
  error: palette.coral,
  info: "#0066cc",

  // Semantic
  border: palette.glassBorder,
  background: palette.night,
  text: "#FFFFFF",
  textSecondary: palette.textMuted,
  textTertiary: palette.textFaint
};

export const lightColors = {
  ...colors
};

export const darkColors = {
  ...colors,
  background: "#1f2937",
  text: "#ffffff",
  textSecondary: "#d1d5db",
  textTertiary: "#9ca3af"
};
