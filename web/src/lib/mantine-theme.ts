import { Badge, createTheme, type MantineColorsTuple } from "@mantine/core";

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

export const mantineTheme = createTheme({
  primaryColor: "brand",
  primaryShade: { light: 6, dark: 5 },
  colors: { brand, navy, blue, cyan, teal, red, orange, yellow, gold, steel, gray, dark },
  /** Yazı Rengi 1. */
  black: "#0a0028",
  // Bundled via @fontsource-variable/inter (imported in main.tsx), including the latin-ext subset for Turkish.
  fontFamily: '"Inter Variable", "Inter", ui-sans-serif, system-ui, sans-serif',
  headings: { fontFamily: '"Inter Variable", "Inter", ui-sans-serif, system-ui, sans-serif' },
  defaultRadius: "md",
  cursorType: "pointer",
  components: {
    // Mantine badges are uppercase by default; the app shows statuses and counts in normal case.
    Badge: Badge.extend({ styles: { root: { textTransform: "none" } } }),
  },
});
