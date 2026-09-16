/**
 * `as const` so `fontWeight` narrows to RN's literal `FontWeight` union (e.g. `"700"`) instead of widening to
 * `string` - lets these presets spread directly into a `StyleSheet.create<TextStyle>()` entry without a cast.
 */
export const typography = {
  h1: {
    fontSize: 32,
    fontWeight: "700",
    lineHeight: 40
  },
  h2: {
    fontSize: 28,
    fontWeight: "700",
    lineHeight: 36
  },
  h3: {
    fontSize: 24,
    fontWeight: "700",
    lineHeight: 32
  },
  h4: {
    fontSize: 20,
    fontWeight: "600",
    lineHeight: 28
  },
  body: {
    fontSize: 16,
    fontWeight: "400",
    lineHeight: 24
  },
  bodySm: {
    fontSize: 14,
    fontWeight: "400",
    lineHeight: 20
  },
  bodyXs: {
    fontSize: 12,
    fontWeight: "400",
    lineHeight: 16
  },
  button: {
    fontSize: 16,
    fontWeight: "600",
    lineHeight: 24
  },
  caption: {
    fontSize: 12,
    fontWeight: "500",
    lineHeight: 16
  }
} as const;
