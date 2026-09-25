import { describe, expect, it } from "vitest";
import { NAV_ITEMS, NAV_SECTIONS, isNavGroup } from "./navigation";

// 11.10: NAV_SECTIONS reuses NAV_ITEMS/APP_LAUNCHER_ITEMS entries by key rather than copying them, so this guards
// against a NAV_ITEMS entry silently falling out of the grouped sidebar (or being listed twice).
describe("NAV_SECTIONS", () => {
  it("includes every NAV_ITEMS entry exactly once", () => {
    const seenKeys: string[] = [];
    for (const section of NAV_SECTIONS) {
      for (const entry of section) {
        if (isNavGroup(entry)) {
          for (const item of entry.items) seenKeys.push(item.key);
        } else {
          seenKeys.push(entry.key);
        }
      }
    }

    for (const item of NAV_ITEMS) {
      expect(seenKeys.filter((key) => key === item.key)).toHaveLength(1);
    }
  });

  it("reuses the same NavItem object (not a copy) for every entry", () => {
    for (const section of NAV_SECTIONS) {
      for (const entry of section) {
        const items = isNavGroup(entry) ? entry.items : [entry];
        for (const item of items) {
          const original = NAV_ITEMS.find((candidate) => candidate.key === item.key);
          if (original) expect(item).toBe(original);
        }
      }
    }
  });
});
