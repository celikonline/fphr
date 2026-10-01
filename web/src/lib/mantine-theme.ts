import {
  Badge,
  Button,
  createTheme,
  defaultVariantColorsResolver,
  Fieldset,
  Input,
  InputWrapper,
  type MantineTheme,
  mergeThemeOverrides,
  Modal,
  type MantineColorsTuple,
  type MantineThemeOverride,
  type VariantColorsResolver,
} from "@mantine/core";

import { DEFAULT_PALETTE, type Palette } from "@/store/ui.store";
import classes from "./mantine-theme.module.css";

/*
 * Corporate palette. Each named color sits at the tuple index Mantine reads for filled components (6 unless noted);
 * the other shades are tints toward white and shades toward black of that color.
 */

/** Kurumsal Mavi (#008CF0) — the primary color. */
const brand: MantineColorsTuple = [
  "#f0f8fe",
  "#cfe9fc",
  "#addafa",
  "#8acaf8",
  "#64b9f6",
  "#39a6f3",
  "#008cf0",
  "#0070c0",
  "#005490",
  "#003860",
];

/** Kurumsal Lacivert (#280087, index 7) — brand mark and emphasis surfaces. */
const navy: MantineColorsTuple = [
  "#f2f0f8",
  "#dbd4eb",
  "#c2b7dd",
  "#a999cf",
  "#8f7ac0",
  "#7258b0",
  "#53339f",
  "#280087",
  "#1c005e",
  "#100036",
];

/** Yardımcı Renk 1 (#0041D2). */
const blue: MantineColorsTuple = [
  "#f0f4fc",
  "#cfdbf7",
  "#adc2f1",
  "#8aa8ea",
  "#648be4",
  "#396cdc",
  "#0041d2",
  "#0034a8",
  "#00277e",
  "#001a54",
];

/** Yardımcı Renk 2 (#52B4F6, index 4). */
const cyan: MantineColorsTuple = [
  "#f5fafe",
  "#d3ecfd",
  "#afdcfb",
  "#88cbf9",
  "#52b4f6",
  "#489ed8",
  "#3e89bb",
  "#34739d",
  "#2b5e80",
  "#214862",
];

/** Yardımcı Renk 3 (#288396). */
const teal: MantineColorsTuple = [
  "#f2f8f9",
  "#d7e8eb",
  "#bad7dd",
  "#9cc6cf",
  "#7cb3bf",
  "#589fae",
  "#288396",
  "#206978",
  "#184f5a",
  "#10343c",
];

/** Buton/Patlangaç Rengi 1 (#D22D41) — also Mantine's error color (red-6). */
const red: MantineColorsTuple = [
  "#fcf2f4",
  "#f7d8db",
  "#f1bcc2",
  "#ea9ea8",
  "#e47f8b",
  "#dc5c6c",
  "#d22d41",
  "#a82434",
  "#7e1b27",
  "#54121a",
];

/** Buton/Patlangaç Rengi 2 (#F06400). */
const orange: MantineColorsTuple = [
  "#fef6f0",
  "#fce2cf",
  "#facdad",
  "#f8b88a",
  "#f6a164",
  "#f38739",
  "#f06400",
  "#c05000",
  "#903c00",
  "#602800",
];

/** Buton/Patlangaç Rengi 3 (#FA9600). */
const yellow: MantineColorsTuple = [
  "#fff9f0",
  "#feebcf",
  "#fdddad",
  "#fdcf8a",
  "#fcbf64",
  "#fbae39",
  "#fa9600",
  "#c87800",
  "#965a00",
  "#643c00",
];

/** Özel Zemin Rengi 1 (#96875A). */
const gold: MantineColorsTuple = [
  "#f9f8f5",
  "#ebe8e0",
  "#ddd9ca",
  "#cfc8b3",
  "#bfb69a",
  "#aea27f",
  "#96875a",
  "#786c48",
  "#5a5136",
  "#3c3624",
];

/** Özel Zemin Rengi 2 (#737882). */
const steel: MantineColorsTuple = [
  "#f7f7f8",
  "#e5e6e8",
  "#d2d4d7",
  "#bfc1c5",
  "#aaadb3",
  "#92969e",
  "#737882",
  "#5c6068",
  "#45484e",
  "#2e3034",
];

/**
 * Light-scheme neutrals from the Zemin and Yazı colors. Mantine reads hover backgrounds from gray-0 (Zemin 3),
 * borders from gray-4 (Zemin 1), placeholders from gray-5 (Yazı 3) and dimmed text from gray-6 (Yazı 2).
 */
const gray: MantineColorsTuple = [
  "#f0f5fa",
  "#e6ebf0",
  "#dce1e6",
  "#d2d7dc",
  "#c8cdd2",
  "#96aabe",
  "#465a6e",
  "#37485a",
  "#283646",
  "#1a2432",
];

/**
 * Dark scheme tinted toward the Yazı 2 slate (Mantine reads text from dark-0, dimmed from dark-2, borders from dark-4,
 * inputs/cards from dark-6 and the page body from dark-7).
 */
const dark: MantineColorsTuple = [
  "#f0f5fa",
  "#c8cdd2",
  "#96aabe",
  "#6e8296",
  "#334357",
  "#28364a",
  "#1c2839",
  "#121c2b",
  "#0c1420",
  "#070c15",
];

/**
 * `variant="default"` (secondary buttons, action icons) is a soft tonal fill instead of a white box with a gray border.
 * The tokens are defined per color scheme in src/styles/index.css.
 */
const variantColorResolver: VariantColorsResolver = (input) => {
  if (input.variant === "default") {
    return {
      background: "var(--sense-tonal-bg)",
      hover: "var(--sense-tonal-hover)",
      color: "var(--sense-tonal-color)",
      border: "calc(0.0625rem * var(--mantine-scale)) solid var(--mantine-color-default-border)",
    };
  }
  return defaultVariantColorsResolver(input);
};

/**
 * Per-palette primary shades. Most corporate colors sit at index 6 (5 in dark for a lighter filled surface);
 * navy's canonical Lacivert is index 7 and its 6 already reads as the "lighter" variant, so both schemes shift up.
 */
const PALETTE_SHADES: Record<Palette, MantineTheme["primaryShade"]> = {
  brand: { light: 6, dark: 5 },
  navy: { light: 7, dark: 6 },
  blue: { light: 6, dark: 5 },
  teal: { light: 6, dark: 5 },
  orange: { light: 6, dark: 5 },
  gold: { light: 6, dark: 5 },
  steel: { light: 6, dark: 5 },
};

/** Representative swatch for palette pickers (the shade used for filled components in the light scheme). */
export const PALETTE_SWATCHES: Record<Palette, string> = {
  brand: brand[6],
  navy: navy[7],
  blue: blue[6],
  teal: teal[6],
  orange: orange[6],
  gold: gold[6],
  steel: steel[6],
};

export const mantineTheme = createTheme({
  primaryColor: DEFAULT_PALETTE,
  variantColorResolver,
  primaryShade: PALETTE_SHADES[DEFAULT_PALETTE],
  colors: { brand, navy, blue, cyan, teal, red, orange, yellow, gold, steel, gray, dark },
  /** Yazı Rengi 1. */
  black: "#0a0028",
  // Body copy: bundled via @fontsource-variable/onest (imported in main.tsx), including the latin-ext subset for Turkish.
  fontFamily: '"Onest Variable", "Onest", ui-sans-serif, system-ui, sans-serif',
  // Display face for Title and other headings: bundled via @fontsource-variable/bricolage-grotesque, latin-ext included.
  headings: {
    fontFamily:
      '"Bricolage Grotesque Variable", "Bricolage Grotesque", ui-sans-serif, system-ui, sans-serif',
    fontWeight: "700",
    sizes: {
      h1: { fontWeight: "800" },
      h2: { fontWeight: "750" },
    },
  },
  defaultRadius: "xs",
  cursorType: "pointer",
  respectReducedMotion: true,
  components: {
    // Mantine badges are uppercase by default; the app shows statuses and counts in normal case.
    Badge: Badge.extend({ styles: { root: { textTransform: "none" } } }),
    // Compact controls with defined edges follow the approved SensePROJECT reference.
    Button: Button.extend({ defaultProps: { radius: "xs" } }),
    Input: Input.extend({ classNames: { wrapper: classes.inputWrapper, input: classes.input } }),
    InputWrapper: InputWrapper.extend({ classNames: { label: classes.label } }),
    Fieldset: Fieldset.extend({ classNames: { root: classes.fieldset, legend: classes.legend } }),
    Modal: Modal.extend({
      defaultProps: { radius: "sm" },
      classNames: { title: classes.modalTitle },
    }),
  },
});

const paletteThemes = new Map<Palette, MantineThemeOverride>();

/**
 * The base theme with `primaryColor` (and its shade) swapped to the user's palette. Memoized per palette so
 * MantineProvider gets a stable object and doesn't recompute CSS variables on unrelated re-renders.
 */
export function mantineThemeForPalette(palette: Palette): MantineThemeOverride {
  if (palette === DEFAULT_PALETTE) return mantineTheme;
  let theme = paletteThemes.get(palette);
  if (!theme) {
    theme = mergeThemeOverrides(mantineTheme, {
      primaryColor: palette,
      primaryShade: PALETTE_SHADES[palette],
    });
    paletteThemes.set(palette, theme);
  }
  return theme;
}
