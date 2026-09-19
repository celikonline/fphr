import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import HttpBackend from "i18next-http-backend";

i18n
  .use(HttpBackend)
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    fallbackLng: "tr",
    // Item 13 (Faz 0): i18n readiness for a third language ahead of ATS. `de` ships as a structural
    // placeholder namespace set ("[DE] ..." markers), not a real translation yet.
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
      "performance",
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
