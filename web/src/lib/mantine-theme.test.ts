import { describe, expect, it } from "vitest";
import { mantineTheme, mantineThemeForPalette, PALETTE_SWATCHES } from "./mantine-theme";
import { PALETTES } from "@/store/ui.store";

describe("mantineThemeForPalette", () => {
  it("returns the base theme for the default palette", () => {
    expect(mantineThemeForPalette("brand")).toBe(mantineTheme);
    expect(mantineTheme.primaryColor).toBe("brand");
  });

  it("swaps primaryColor to the palette and keeps the rest of the theme", () => {
    const theme = mantineThemeForPalette("navy");
    expect(theme.primaryColor).toBe("navy");
    expect(theme.primaryShade).toEqual({ light: 7, dark: 6 });
    expect(theme.colors?.brand).toEqual(mantineTheme.colors?.brand);
    expect(theme.components?.Button).toBe(mantineTheme.components?.Button);
  });

  it("memoizes per palette and has a swatch for every palette", () => {
    expect(mantineThemeForPalette("teal")).toBe(mantineThemeForPalette("teal"));
    for (const palette of PALETTES) {
      expect(mantineTheme.colors?.[palette]).toBeDefined();
      expect(PALETTE_SWATCHES[palette]).toMatch(/^#[0-9a-f]{6}$/);
    }
  });
});
