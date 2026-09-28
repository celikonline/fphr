import AsyncStorage from "@react-native-async-storage/async-storage";
import { getLocales } from "expo-localization";

type I18nModule = typeof import("@/lib/i18n");
type DeviceLocale = { languageCode: string | null; languageTag: string };

const TR: DeviceLocale = { languageCode: "tr", languageTag: "tr-TR" };
const EN: DeviceLocale = { languageCode: "en", languageTag: "en-US" };
const DE: DeviceLocale = { languageCode: "de", languageTag: "de-DE" };

const mockedGetLocales = jest.mocked(getLocales);

/**
 * Loads a fresh `@/lib/i18n` (fresh i18next instance) on a device with the given locales - i.e. a
 * cold app start. AsyncStorage is the shared jest mock, so what one "start" saved is what the next
 * one reads. `getLocales` throwing simulates a dev client built before `expo-localization` existed.
 */
function coldStart(device: DeviceLocale[] | "throws"): I18nModule {
  mockedGetLocales.mockImplementation(() => {
    if (device === "throws") throw new Error("native module missing");
    return device as unknown as ReturnType<typeof getLocales>;
  });
  let loaded: I18nModule | undefined;
  jest.isolateModules(() => {
    loaded = jest.requireActual<I18nModule>("@/lib/i18n");
  });
  return loaded!;
}

beforeEach(async () => {
  await AsyncStorage.clear();
});

afterEach(() => {
  mockedGetLocales.mockImplementation(() => [TR] as unknown as ReturnType<typeof getLocales>);
});

describe("toSupportedLanguage", () => {
  it("maps a locale tag or code onto a supported language", () => {
    const i18nModule = coldStart([TR]);
    expect(i18nModule.toSupportedLanguage("en-GB")).toBe("en");
    expect(i18nModule.toSupportedLanguage("TR")).toBe("tr");
    expect(i18nModule.toSupportedLanguage("tr_TR")).toBe("tr");
  });

  it("returns null for an unsupported or missing value", () => {
    const i18nModule = coldStart([TR]);
    expect(i18nModule.toSupportedLanguage("de")).toBeNull();
    expect(i18nModule.toSupportedLanguage(null)).toBeNull();
    expect(i18nModule.toSupportedLanguage("")).toBeNull();
  });
});

describe("deviceLanguage (first start, nothing saved)", () => {
  it("opens in English on an English device", () => {
    expect(coldStart([EN]).deviceLanguage()).toBe("en");
  });

  it("opens in Turkish on a device language the app does not support", () => {
    expect(coldStart([DE]).deviceLanguage()).toBe("tr");
  });

  it("uses the first supported language of the device's preference list", () => {
    expect(coldStart([DE, EN, TR]).deviceLanguage()).toBe("en");
  });

  it("falls back to Turkish when the native module is unavailable", () => {
    expect(coldStart("throws").deviceLanguage()).toBe("tr");
  });

  it("initializes i18next in the device language before anything is loaded", () => {
    const i18nModule = coldStart([EN]);
    expect(i18nModule.default.language).toBe("en");
    expect(i18nModule.default.t("common:actions.save")).toBe("Save");
  });
});

describe("loadInitialLanguage", () => {
  it("uses the device language when no choice is saved", async () => {
    const i18nModule = coldStart([EN]);

    await expect(i18nModule.loadInitialLanguage()).resolves.toBe("en");
    expect(i18nModule.currentLanguage()).toBe("en");
  });

  it("prefers the saved choice over the device language", async () => {
    await AsyncStorage.setItem("language", "tr");
    const i18nModule = coldStart([EN]);

    await expect(i18nModule.loadInitialLanguage()).resolves.toBe("tr");
    expect(i18nModule.default.t("common:actions.save")).toBe("Kaydet");
  });

  it("ignores a saved value that is not a supported language", async () => {
    await AsyncStorage.setItem("language", "xx");
    const i18nModule = coldStart([EN]);

    await expect(i18nModule.loadInitialLanguage()).resolves.toBe("en");
  });
});

describe("changeAppLanguage (Profil > Dil)", () => {
  it("switches every listener immediately, without a restart", async () => {
    const i18nModule = coldStart([TR]);
    const listener = jest.fn();
    i18nModule.default.on("languageChanged", listener);

    await i18nModule.changeAppLanguage("en");

    expect(listener).toHaveBeenCalledWith("en");
    expect(i18nModule.currentLanguage()).toBe("en");
    expect(i18nModule.default.t("common:actions.save")).toBe("Save");
  });

  it("persists the choice so it survives an app restart", async () => {
    await coldStart([TR]).changeAppLanguage("en");
    expect(await AsyncStorage.getItem("language")).toBe("en");

    // Restart on the same Turkish device: the saved choice wins over the device language.
    await expect(coldStart([TR]).loadInitialLanguage()).resolves.toBe("en");
  });
});
