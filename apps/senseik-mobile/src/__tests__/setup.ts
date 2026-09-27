// Jest setup file
jest.mock("@react-native-async-storage/async-storage", () =>
  jest.requireActual("@react-native-async-storage/async-storage/jest/async-storage-mock")
);

jest.mock("expo-secure-store", () => ({
  setItemAsync: jest.fn(() => Promise.resolve()),
  getItemAsync: jest.fn(() => Promise.resolve(null)),
  deleteItemAsync: jest.fn(() => Promise.resolve())
}));

jest.mock("expo-local-authentication", () => ({
  hasHardwareAsync: jest.fn(() => Promise.resolve(true)),
  isEnrolledAsync: jest.fn(() => Promise.resolve(true)),
  supportedAuthenticationTypesAsync: jest.fn(() => Promise.resolve([])),
  AuthenticationType: {
    FINGERPRINT: 1,
    FACIAL_RECOGNITION: 2,
    IRIS: 3
  },
  authenticateAsync: jest.fn(() => Promise.resolve({ success: true }))
}));

jest.mock("expo-notifications", () => ({
  getExpoPushTokenAsync: jest.fn(() => Promise.resolve({ data: "fake-token" })),
  requestPermissionsAsync: jest.fn(() => Promise.resolve({ status: "granted" })),
  setNotificationHandler: jest.fn(),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  addNotificationResponseReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  removeNotificationSubscription: jest.fn(),
  scheduleNotificationAsync: jest.fn()
}));

jest.mock("expo-font", () => ({
  useFonts: jest.fn(() => [true, null]),
  loadAsync: jest.fn(() => Promise.resolve())
}));

jest.mock("@expo-google-fonts/manrope", () => ({
  Manrope_400Regular: "Manrope_400Regular",
  Manrope_500Medium: "Manrope_500Medium",
  Manrope_600SemiBold: "Manrope_600SemiBold",
  Manrope_700Bold: "Manrope_700Bold",
  Manrope_800ExtraBold: "Manrope_800ExtraBold"
}));

jest.mock("react-native-svg", () => {
  const { View } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: View,
    Svg: View,
    Path: View
  };
});

jest.mock("react-native-maps", () => {
  const { View } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: View,
    Marker: View,
    Circle: View
  };
});
