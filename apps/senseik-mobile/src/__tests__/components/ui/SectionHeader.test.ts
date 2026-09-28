import { sectionTitleText } from "@/components/ui/SectionHeader";

describe("sectionTitleText", () => {
  it("uppercases with Turkish casing rules (dotted İ stays İ, not ASCII I)", () => {
    expect(sectionTitleText("İşlemlerim")).toBe("İŞLEMLERİM");
  });

  it("uppercases a full sentence, keeping dotted i as İ", () => {
    expect(sectionTitleText("Bugün izinde olanlar")).toBe("BUGÜN İZİNDE OLANLAR");
  });
});
