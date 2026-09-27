import { fontFor, fonts, palette, tones, accents } from "@/theme/theme";

describe("theme", () => {
  it("maps each weight to its Manrope family", () => {
    expect(fontFor(400)).toEqual({ fontFamily: "Manrope_400Regular" });
    expect(fontFor(800)).toEqual({ fontFamily: "Manrope_800ExtraBold" });
    expect(Object.keys(fonts)).toHaveLength(5);
  });
  it("keeps the login screen palette values", () => {
    expect(palette.night).toBe("#0E0033");
    expect(palette.navy).toBe("#280087");
    expect(palette.brand).toBe("#008CF0");
  });
  it("defines fg/bg for every tone and accent", () => {
    for (const t of Object.values(tones)) expect(t.fg && t.bg).toBeTruthy();
    expect(Object.keys(accents)).toHaveLength(9);
  });
});
