import testI18n from "@/lib/i18n";

// Jest setup file
jest.mock("@react-native-async-storage/async-storage", () =>
  jest.requireActual("@react-native-async-storage/async-storage/jest/async-storage-mock")
);

jest.mock("expo-secure-store", () => ({
  setItemAsync: jest.fn(() => Promise.resolve()),
  getItemAsync: jest.fn(() => Promise.resolve(null)),
  deleteItemAsync: jest.fn(() => Promise.resolve()),
}));

jest.mock("expo-local-authentication", () => ({
  hasHardwareAsync: jest.fn(() => Promise.resolve(true)),
  isEnrolledAsync: jest.fn(() => Promise.resolve(true)),
  supportedAuthenticationTypesAsync: jest.fn(() => Promise.resolve([])),
  AuthenticationType: {
    FINGERPRINT: 1,
    FACIAL_RECOGNITION: 2,
    IRIS: 3,
  },
  authenticateAsync: jest.fn(() => Promise.resolve({ success: true })),
}));

jest.mock("expo-notifications", () => ({
  getExpoPushTokenAsync: jest.fn(() => Promise.resolve({ data: "fake-token" })),
  requestPermissionsAsync: jest.fn(() => Promise.resolve({ status: "granted" })),
  setNotificationHandler: jest.fn(),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  removeNotificationSubscription: jest.fn(),
  scheduleNotificationAsync: jest.fn(),
}));

jest.mock("expo-font", () => ({
  useFonts: jest.fn(() => [true, null]),
  loadAsync: jest.fn(() => Promise.resolve()),
}));

jest.mock("@expo-google-fonts/manrope", () => ({
  Manrope_400Regular: "Manrope_400Regular",
  Manrope_500Medium: "Manrope_500Medium",
  Manrope_600SemiBold: "Manrope_600SemiBold",
  Manrope_700Bold: "Manrope_700Bold",
  Manrope_800ExtraBold: "Manrope_800ExtraBold",
}));

jest.mock("react-native-svg", () => {
  const { View } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: View,
    Svg: View,
    Path: View,
  };
});

jest.mock("react-native-maps", () => {
  const { View } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: View,
    Marker: View,
    Circle: View,
  };
});

// react-native-reanimated v4's shipped `/mock` entry pulls in `react-native-worklets`' native
// module bootstrap and throws outside a real app runtime, so it is unusable under jest-expo as-is
// (see LoginScreen.test.tsx). A handful of components (BottomSheet, AuthBackdrop, NotchedTabBar,
// HomeMenu, ...) use it for entrance animations / `useReducedMotion` - stub the few APIs they need
// globally so any test that transitively imports one of them (e.g. via `@/components/ui`) doesn't
// have to know about reanimated. Tests exercising `useReducedMotion` directly can still override
// this with their own `jest.mock("react-native-reanimated", ...)`.
jest.mock("react-native-reanimated", () => {
  const { View, Text } = jest.requireActual("react-native");

  function chainableTiming() {
    const timing: Record<string, unknown> = {};
    timing.delay = () => chainableTiming();
    timing.duration = () => chainableTiming();
    return timing;
  }

  return {
    __esModule: true,
    default: { View, Text },
    useSharedValue: (initial: unknown) => ({ value: initial }),
    useAnimatedStyle: (factory: () => Record<string, unknown>) => factory(),
    useReducedMotion: () => false,
    withSpring: (toValue: unknown) => toValue,
    withTiming: (toValue: unknown) => toValue,
    withDelay: (_delay: number, animation: unknown) => animation,
    withRepeat: (animation: unknown) => animation,
    withSequence: (...animations: unknown[]) => animations[0],
    FadeInDown: chainableTiming(),
  };
});

// FG-001: tests run in Turkish regardless of the host machine's locale - the device language is
// pinned to "tr" (nothing is saved in the AsyncStorage mock), so every existing Turkish assertion
// keeps matching. A test that switches to English must not leak into the next one.
// `getLocales` is a jest.fn so a test can simulate another device language (see i18n.test.ts).
jest.mock("expo-localization", () => ({
  getLocales: jest.fn(() => [{ languageCode: "tr", languageTag: "tr-TR" }]),
}));

beforeEach(() => {
  if (testI18n.language !== "tr") {
    void testI18n.changeLanguage("tr");
  }
});
