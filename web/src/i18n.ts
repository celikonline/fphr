import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import HttpBackend from "i18next-http-backend";
import { applyDocumentLang } from "@/lib/document-lang";

i18n.on("languageChanged", (language) => applyDocumentLang(language));

i18n
  .use(HttpBackend)
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    fallbackLng: "tr",
    // Item 13 (Faz 0): i18n readiness for a third language ahead of ATS. `de` is a full translation:
    // every namespace has key parity with `tr`/`en`, no "[DE] ..." placeholders remain.
    supportedLngs: ["tr", "en", "de"],
    defaultNS: "common",
    ns: [
      "common",
      "auth",
      "navigation",
      "home",
      "account",
      "settings",
      "employees",
      "structure",
      "dataTransfer",
      "company",
      "locations",
      "users",
      "audit",
      "workflow",
      "notifications",
      "leave",
      "attendance",
      "recruitment",
      "documents",
      "assets",
      "trust",
      "requests",
      "expense",
      "reports",
      "performance",
      "payroll",
      "support",
      "companyCalendar",
      "ohs",
      "platform",
      "setup",
    ],
    interpolation: {
      escapeValue: false,
    },
    backend: {
      loadPath: "/locales/{{lng}}/{{ns}}.json",
    },
    detection: {
      order: ["localStorage", "navigator"],
      caches: ["localStorage"],
    },
  });

export default i18n;
