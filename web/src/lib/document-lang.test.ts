import { describe, expect, it } from "vitest";
import i18n from "i18next";
import { applyDocumentLang } from "./document-lang";

describe("applyDocumentLang", () => {
  it("sets the primary language subtag on <html>", () => {
    applyDocumentLang("en-US");
    expect(document.documentElement.lang).toBe("en");
    applyDocumentLang("de");
    expect(document.documentElement.lang).toBe("de");
  });

  it("leaves the attribute alone when no language is known", () => {
    document.documentElement.lang = "tr";
    applyDocumentLang(undefined);
    expect(document.documentElement.lang).toBe("tr");
  });

  it("follows i18next language changes when wired to languageChanged", async () => {
    const instance = i18n.createInstance();
    instance.on("languageChanged", (lng) => applyDocumentLang(lng));
    await instance.init({ lng: "tr", resources: { tr: { translation: {} }, en: { translation: {} } } });
    expect(document.documentElement.lang).toBe("tr");
    await instance.changeLanguage("en");
    expect(document.documentElement.lang).toBe("en");
  });
});
