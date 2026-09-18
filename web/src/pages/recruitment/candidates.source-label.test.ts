import { describe, expect, it } from "vitest";
import i18next from "i18next";
import { enumLabel } from "@/lib/i18n-enum";
import trRecruitment from "../../../public/locales/tr/recruitment.json";
import enRecruitment from "../../../public/locales/en/recruitment.json";

/**
 * senseik-008 Bug 2 regression: `CandidatesPage` renders the source column via
 * `enumLabel(t, "recruitment:candidates.sources", candidate.source)` (see candidates.tsx). The backend used to
 * serialize `Source` PascalCase ("Manual") via `.ToString()` instead of leaving it typed as the enum for the
 * global camelCase `JsonStringEnumConverter` (fixed in `CandidateQueries.cs`/`CandidateDtos.cs` as part of this
 * card) — this test proves the web renders the correct label either way, so it stays correct even if a similar
 * backend regression recurs elsewhere in the same DTO family (JobPostingQueries etc., out of scope here).
 */
function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "recruitment",
    ns: ["recruitment"],
    resources: { tr: { recruitment: trRecruitment }, en: { recruitment: enRecruitment } },
    interpolation: { escapeValue: false },
  });
  return instance;
}

describe("candidates source label (senseik-008 Bug 2)", () => {
  it("resolves the correct label for the PascalCase value the buggy backend used to send", () => {
    const i18n = createTestI18n();
    const label = enumLabel(i18n.t.bind(i18n), "recruitment:candidates.sources", "Manual");
    expect(label).toBe("Elle eklendi");
    expect(label).not.toBe("Manual");
    expect(label).not.toMatch(/^recruitment:/);
  });

  it("still resolves correctly once the backend is fixed and sends camelCase", () => {
    const i18n = createTestI18n();
    const label = enumLabel(i18n.t.bind(i18n), "recruitment:candidates.sources", "manual");
    expect(label).toBe("Elle eklendi");
  });

  it("never leaks the raw i18n key for any of the four known sources, in either casing", () => {
    const i18n = createTestI18n();
    const cases: Array<[string, string]> = [
      ["Manual", "Elle eklendi"],
      ["CareerSite", "Kariyer sitesi"],
      ["Referral", "Referans"],
      ["Other", "Diğer"],
    ];
    for (const [raw, expected] of cases) {
      const label = enumLabel(i18n.t.bind(i18n), "recruitment:candidates.sources", raw);
      expect(label).toBe(expected);
      expect(label).not.toMatch(/^recruitment:candidates\.sources\./);
    }
  });

  it("resolves against the en bundle too (locale files stay in sync, see i18n.locales.test.ts)", async () => {
    const i18n = createTestI18n();
    await i18n.changeLanguage("en");
    const label = enumLabel(i18n.t.bind(i18n), "recruitment:candidates.sources", "Manual");
    expect(label).toBe("Added manually");
  });
});
