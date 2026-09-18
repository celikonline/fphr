import { beforeEach, describe, expect, it } from "vitest";
import { DEFAULT_PALETTE, PALETTES, resolvePalette, useUIStore } from "./ui.store";

describe("ui.store palette", () => {
  beforeEach(() => {
    localStorage.clear();
    useUIStore.setState({ theme: "system", palette: DEFAULT_PALETTE });
  });

  it("defaults to the corporate brand palette", () => {
    expect(useUIStore.getState().palette).toBe("brand");
  });

  it("stores a selected palette and persists it alongside the theme", () => {
    useUIStore.getState().setPalette("teal");
    expect(useUIStore.getState().palette).toBe("teal");
    const persisted = JSON.parse(localStorage.getItem("ui-store") ?? "{}") as { state?: Record<string, unknown> };
    expect(persisted.state).toEqual({ theme: "system", palette: "teal" });
  });

  it("falls back to the default for unknown persisted values", () => {
    expect(resolvePalette("cyan")).toBe(DEFAULT_PALETTE);
    expect(resolvePalette(undefined)).toBe(DEFAULT_PALETTE);
    for (const palette of PALETTES) expect(resolvePalette(palette)).toBe(palette);
    useUIStore.getState().setPalette("nope" as never);
    expect(useUIStore.getState().palette).toBe(DEFAULT_PALETTE);
  });
});
