import { createTheme, type CSSVariablesResolver, type MantineColorsTuple } from "@mantine/core";

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

export const mantineTheme = createTheme({
  primaryColor: "brandBlue",
  primaryShade: { light: 6, dark: 5 },
  colors: { brandBlue, algosenseGreen },
  fontFamily: '"Inter", ui-sans-serif, system-ui, sans-serif',
  headings: { fontFamily: '"Inter", ui-sans-serif, system-ui, sans-serif' },
  defaultRadius: "md",
  cursorType: "pointer",
});

/**
 * Points Mantine's surface/text/border variables at the app tokens in `styles/index.css`, so Mantine
 * components and the remaining Tailwind markup share one palette in both color schemes.
 */
export const mantineCssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {},
  light: {
    "--mantine-color-body": "var(--background)",
    "--mantine-color-text": "var(--foreground)",
    "--mantine-color-dimmed": "var(--muted-foreground)",
    "--mantine-color-default-border": "var(--border)",
  },
  dark: {
    "--mantine-color-body": "var(--background)",
    "--mantine-color-text": "var(--foreground)",
    "--mantine-color-dimmed": "var(--muted-foreground)",
    "--mantine-color-default-border": "var(--border)",
  },
});
