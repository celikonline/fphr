import { contentBottomInset, buildNotchPath, TAB_BAR_HEIGHT } from "@/navigation/layout";

const MOCKUP_NOTCH_PATH =
  "M0 24 Q0 0 24 0 H136 C148 0 152 3 155 12 C161 34 174 50 195 50 C216 50 229 34 235 12 C238 3 242 0 254 0 H366 Q390 0 390 24 V92 H0 Z";

describe("contentBottomInset", () => {
  it("adds tab bar, safe area and breathing room", () =>
    expect(contentBottomInset(24)).toBe(TAB_BAR_HEIGHT + 24 + 16));
});

describe("buildNotchPath", () => {
  it("builds a closed path spanning the width with a centred notch", () => {
    const d = buildNotchPath(390, 92);
    expect(d.startsWith("M0 ")).toBe(true);
    expect(d.endsWith("Z")).toBe(true);
    expect(d).toContain("195"); // notch centre x
  });

  it("scales the notch centre with the width", () =>
    expect(buildNotchPath(412, 92)).toContain("206"));

  it("reproduces the mockup path exactly at the mockup's 390x92 size", () =>
    expect(buildNotchPath(390, 92)).toBe(MOCKUP_NOTCH_PATH));

  it("closes the bar at the requested height", () =>
    expect(buildNotchPath(390, 106).endsWith("V106 H0 Z")).toBe(true));
});
