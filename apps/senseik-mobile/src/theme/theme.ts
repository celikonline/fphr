import type { TextStyle } from "react-native";

export const palette = {
  night: "#0E0033",
  navy: "#280087",
  brand: "#008CF0",
  brandText: "#5CBBFF",
  ice: "#D3ECFD",
  coral: "#FF8A80",
  badge: "#FF5C6C",
  white: "#FFFFFF",
  glass: "rgba(255,255,255,0.07)",
  glassBorder: "rgba(255,255,255,0.14)",
  glassInner: "rgba(255,255,255,0.06)",
  line: "rgba(255,255,255,0.10)",
  textMuted: "rgba(211,236,253,0.72)",
  textFaint: "rgba(211,236,253,0.45)",
  sheetTop: "#24107A",
  sheetBottom: "#170553",
  tabBar: "#1A0766",
  scrim: "rgba(5,0,20,0.55)",
  // ScreenBackground's decorative brand-mark ghost squares (border/fill) - distinct alphas from
  // `glass`/`glassBorder` so they read as a subtler background trace, not a foreground surface.
  ghostBorder: "rgba(255,255,255,0.18)",
  ghostFill: "rgba(0,140,240,0.10)",
  // BottomSheet's grab handle - brighter than `glassBorder` so it stays visible against the sheet
  // gradient.
  grabber: "rgba(255,255,255,0.25)",
  // ErrorBanner's border - pairs with `tones.danger.bg` (same hue) at a higher alpha for definition.
  dangerBorder: "rgba(255,138,128,0.3)",
} as const;

// AuthBackdrop.tsx'teki `experimental_backgroundImage` sabitiyle birebir aynı - orası bu sabiti
// import eder, iki gradyan tanımı asla birbirinden sapmasın diye.
export const backdropImage =
  "radial-gradient(circle at 90% 5%, rgba(0,140,240,0.55), transparent 45%), radial-gradient(circle at 0% 100%, rgba(0,65,210,0.75), transparent 55%), linear-gradient(160deg, #280087, #0E0033)";

export type Tone = "success" | "warning" | "danger" | "neutral" | "info";

export const tones: Record<Tone, { fg: string; bg: string }> = {
  success: { fg: "#4FD9A2", bg: "rgba(46,204,142,0.18)" },
  warning: { fg: "#FFCB5C", bg: "rgba(255,196,77,0.18)" },
  danger: { fg: "#FF9A8F", bg: "rgba(255,138,128,0.18)" },
  neutral: { fg: "#C6D6EC", bg: "rgba(211,236,253,0.12)" },
  info: { fg: "#5CBBFF", bg: "rgba(0,140,240,0.22)" },
};

export type AccentKey =
  "blue" | "green" | "coral" | "purple" | "amber" | "teal" | "indigo" | "pink" | "gray";

export const accents: Record<AccentKey, { fg: string; bg: string }> = {
  blue: { fg: "#5CBBFF", bg: "rgba(0,140,240,0.22)" },
  green: { fg: "#4FD9A2", bg: "rgba(46,204,142,0.18)" },
  coral: { fg: "#FF9A8F", bg: "rgba(255,138,128,0.18)" },
  purple: { fg: "#B19BFF", bg: "rgba(150,120,255,0.22)" },
  amber: { fg: "#FFCB5C", bg: "rgba(255,196,77,0.18)" },
  teal: { fg: "#4DDFD1", bg: "rgba(56,214,200,0.18)" },
  indigo: { fg: "#9AA8FF", bg: "rgba(120,140,255,0.22)" },
  pink: { fg: "#FF94C2", bg: "rgba(255,120,180,0.18)" },
  gray: { fg: "#C6D6EC", bg: "rgba(211,236,253,0.12)" },
};

export type FontWeightKey = 400 | 500 | 600 | 700 | 800;

export const fonts: Record<FontWeightKey, string> = {
  400: "Manrope_400Regular",
  500: "Manrope_500Medium",
  600: "Manrope_600SemiBold",
  700: "Manrope_700Bold",
  800: "Manrope_800ExtraBold",
};

export function fontFor(weight: FontWeightKey): { fontFamily: string } {
  return { fontFamily: fonts[weight] };
}

export const radius = {
  card: 20,
  tile: 22,
  button: 14,
  pill: 999,
  sheet: 28,
} as const;

export const text: {
  display: TextStyle;
  title: TextStyle;
  heading: TextStyle;
  section: TextStyle;
  body: TextStyle;
  bodyMuted: TextStyle;
  caption: TextStyle;
  number: TextStyle;
  button: TextStyle;
} = {
  display: {
    fontSize: 28,
    lineHeight: 34,
    letterSpacing: -0.5,
    color: palette.white,
    ...fontFor(800),
  },
  title: {
    fontSize: 22,
    lineHeight: 28,
    letterSpacing: -0.4,
    color: palette.white,
    ...fontFor(800),
  },
  heading: { fontSize: 16, lineHeight: 22, color: palette.white, ...fontFor(800) },
  section: {
    fontSize: 12,
    lineHeight: 16,
    textTransform: "uppercase",
    letterSpacing: 1.7,
    color: palette.textMuted,
    ...fontFor(800),
  },
  body: { fontSize: 15, lineHeight: 22, color: palette.white, ...fontFor(500) },
  bodyMuted: { fontSize: 14, lineHeight: 20, color: palette.textMuted, ...fontFor(600) },
  caption: { fontSize: 12, lineHeight: 16, color: palette.textMuted, ...fontFor(600) },
  number: {
    fontSize: 24,
    lineHeight: 30,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(800),
  },
  button: { fontSize: 16, lineHeight: 22, color: palette.white, ...fontFor(800) },
};
