import { initialsOf, accentForName } from "@/components/ui/avatarUtils";

describe("initialsOf", () => {
  it.each([
    ["Ali Şahin", "AŞ"],
    ["İlkay Şahin-Öztürk", "İŞ"],
    ["Ece", "E"],
    ["  ", "?"],
    ["", "?"],
  ])("initialsOf(%s)", (name, expected) => {
    expect(initialsOf(name)).toBe(expected);
  });
});

describe("accentForName", () => {
  it("is stable and within the 9 accents", () => {
    expect(accentForName("Selin Aydın")).toBe(accentForName("Selin Aydın"));
    expect([
      "blue",
      "green",
      "coral",
      "purple",
      "amber",
      "teal",
      "indigo",
      "pink",
      "gray",
    ]).toContain(accentForName("Murat Kaya"));
  });
});
