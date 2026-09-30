import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import AsyncStorage from "@react-native-async-storage/async-storage";
import { getLocales } from "expo-localization";
import { PreferenceStorage } from "./storage";
import tr from "@/locales/tr";
import en from "@/locales/en";

/**
 * FG-001: mobile i18n. Bundles are imported statically (no http backend - the app must render
 * offline), so `init` is synchronous and `t()` works as soon as this module is loaded.
 *
 * Language source priority (plan §2 Karar 18): the user's saved choice > device language (if it
 * is a supported one) > Turkish.
 */
export const SUPPORTED_LANGUAGES = ["tr", "en"] as const;
export type AppLanguage = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: AppLanguage = "tr";

export const NAMESPACES = Object.keys(tr) as (keyof typeof tr)[];

/**
 * Same AsyncStorage key `PreferenceStorage.setLanguage` writes. Read raw because
 * `PreferenceStorage.getLanguage()` returns "tr" when nothing is stored, which would hide the
 * "no saved choice -> use the device language" case; `storage.ts` itself stays unchanged.
 */
const LANGUAGE_STORAGE_KEY = "language";

export function toSupportedLanguage(value: string | null | undefined): AppLanguage | null {
  const code = value?.toLowerCase().split(/[-_]/)[0];
  return SUPPORTED_LANGUAGES.find((language) => language === code) ?? null;
}

/** First device locale that the app supports; Turkish when none is. */
export function deviceLanguage(): AppLanguage {
  try {
    for (const locale of getLocales()) {
      const supported = toSupportedLanguage(locale.languageCode ?? locale.languageTag);
      if (supported) return supported;
    }
  } catch {
    // Native module unavailable (e.g. an outdated dev client) - fall back to the default.
  }
  return DEFAULT_LANGUAGE;
}

async function storedLanguage(): Promise<AppLanguage | null> {
  try {
    return toSupportedLanguage(await AsyncStorage.getItem(LANGUAGE_STORAGE_KEY));
  } catch {
    return null;
  }
}

/** Resolves the startup language (saved choice > device > tr) and activates it. */
export async function loadInitialLanguage(): Promise<AppLanguage> {
  const language = (await storedLanguage()) ?? deviceLanguage();
  if (i18n.language !== language) {
    await i18n.changeLanguage(language);
  }
  return language;
}

/** Profile > Dil: switches every mounted screen immediately and persists the choice. */
export async function changeAppLanguage(language: AppLanguage): Promise<void> {
  await i18n.changeLanguage(language);
  await PreferenceStorage.setLanguage(language);
}

/** The active app language (never an unsupported value). */
export function currentLanguage(): AppLanguage {
  return toSupportedLanguage(i18n.language) ?? DEFAULT_LANGUAGE;
}

i18n.use(initReactI18next).init({
  resources: { tr, en },
  lng: deviceLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: [...SUPPORTED_LANGUAGES],
  ns: NAMESPACES,
  defaultNS: "common",
  initAsync: false,
  interpolation: { escapeValue: false },
  returnNull: false,
});

export default i18n;
