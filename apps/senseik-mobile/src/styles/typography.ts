import { fontFor } from "@/theme/theme";

/**
 * `as const` so these presets keep their literal shape - lets them spread directly into a
 * `StyleSheet.create<TextStyle>()` entry without a cast. `fontFamily` (via `fontFor`) replaces the
 * old `fontWeight` field, since the Manrope font files each ship as a distinct weight-specific family.
 */
export const typography = {
  h1: {
    fontSize: 32,
    lineHeight: 40,
    ...fontFor(700),
  },
  h2: {
    fontSize: 28,
    lineHeight: 36,
    ...fontFor(700),
  },
  h3: {
    fontSize: 24,
    lineHeight: 32,
    ...fontFor(700),
  },
  h4: {
    fontSize: 20,
    lineHeight: 28,
    ...fontFor(600),
  },
  body: {
    fontSize: 16,
    lineHeight: 24,
    ...fontFor(400),
  },
  bodySm: {
    fontSize: 14,
    lineHeight: 20,
    ...fontFor(400),
  },
  bodyXs: {
    fontSize: 12,
    lineHeight: 16,
    ...fontFor(400),
  },
  button: {
    fontSize: 16,
    lineHeight: 24,
    ...fontFor(600),
  },
  caption: {
    fontSize: 12,
    lineHeight: 16,
    ...fontFor(500),
  },
} as const;
