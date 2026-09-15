import { createTheme, type MantineColorsTuple } from "@mantine/core";

/** App primary blue (the `--primary` token, hsl(217 91% 60%) = #3b82f6 at index 5). */
const brandBlue: MantineColorsTuple = [
  "#eff6ff",
  "#dbeafe",
  "#bfdbfe",
  "#93c5fd",
  "#60a5fa",
  "#3b82f6",
  "#2563eb",
  "#1d4ed8",
  "#1e40af",
  "#1e3a8a",
];

/** Algosense brand green (`--brand`, #17AE67) for marketing surfaces such as the login page. */
const algosenseGreen: MantineColorsTuple = [
  "#e8faf1",
  "#c9f2de",
  "#96e5bf",
  "#5fd69d",
  "#33c883",
  "#17ae67",
  "#0f8a52",
  "#0c6f42",
  "#095533",
  "#063c24",
];

/**
 * Neutral near-black dark scheme (Mantine reads text from dark-0, dimmed from dark-2, borders from dark-4,
 * inputs/cards from dark-6 and the page body from dark-7), close to the app's previous dark look.
 */
const neutralDark: MantineColorsTuple = [
  "#e5e5e5",
  "#c4c4c4",
  "#a3a3a3",
  "#737373",
  "#2e2e2e",
  "#242424",
  "#171717",
  "#0a0a0a",
  "#050505",
  "#000000",
];

export const mantineTheme = createTheme({
  primaryColor: "brandBlue",
  primaryShade: { light: 6, dark: 5 },
  colors: { brandBlue, algosenseGreen, dark: neutralDark },
  fontFamily: '"Inter", ui-sans-serif, system-ui, sans-serif',
  headings: { fontFamily: '"Inter", ui-sans-serif, system-ui, sans-serif' },
  defaultRadius: "md",
  cursorType: "pointer",
});
