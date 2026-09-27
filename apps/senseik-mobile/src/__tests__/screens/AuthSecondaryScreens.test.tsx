import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Alert, TextInput } from "react-native";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import ForgotPasswordScreen from "@/screens/auth/ForgotPasswordScreen";
import BiometricPromptScreen from "@/screens/auth/BiometricPromptScreen";
import { useAuthStore } from "@/store/authStore";
import type { AuthStackParamList } from "@/types/navigation";

// Same stub as LoginScreen.test: reanimated v4's real module throws outside a native runtime.
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
// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("@/store/authStore");

function render(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

function findButton(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

describe("ForgotPasswordScreen", () => {
  type Props = NativeStackScreenProps<AuthStackParamList, "ForgotPassword">;
  const goBack = jest.fn();
  const props = { navigation: { goBack }, route: {} } as unknown as Props;

  beforeEach(() => {
    goBack.mockClear();
    jest.spyOn(Alert, "alert").mockImplementation(() => undefined);
  });

  afterEach(() => jest.restoreAllMocks());

  it("asks for the e-mail and stays on the screen when it is empty", () => {
    const renderer = render(<ForgotPasswordScreen {...props} />);

    act(() => findButton(renderer, "Sıfırlama bağlantısı gönder").props.onPress());

    expect(Alert.alert).toHaveBeenCalledWith("Hata", "Lütfen e-posta adresinizi girin");
    expect(goBack).not.toHaveBeenCalled();
  });

  it("confirms generically and returns to login once an e-mail is entered", () => {
    const renderer = render(<ForgotPasswordScreen {...props} />);

    act(() => renderer.root.findByType(TextInput).props.onChangeText("ad@sirket.com"));
    act(() => findButton(renderer, "Sıfırlama bağlantısı gönder").props.onPress());

    expect(Alert.alert).toHaveBeenCalledWith(
      "Başarılı",
      "E-posta kayıtlıysa sıfırlama bağlantısı gönderildi."
    );
    expect(goBack).toHaveBeenCalledTimes(1);
  });

  it("goes back to login from the link", () => {
    const renderer = render(<ForgotPasswordScreen {...props} />);

    act(() => findButton(renderer, "Girişe dön").props.onPress());

    expect(goBack).toHaveBeenCalledTimes(1);
  });
});

describe("BiometricPromptScreen", () => {
  type Props = NativeStackScreenProps<AuthStackParamList, "BiometricPrompt">;
  const goBack = jest.fn();
  const loginWithBiometric = jest.fn();
  const props = {
    navigation: { goBack },
    route: { params: { email: "ad@sirket.com" } },
  } as unknown as Props;

  beforeEach(() => {
    goBack.mockClear();
    loginWithBiometric.mockReset();
    (useAuthStore as unknown as jest.Mock).mockReturnValue({ loginWithBiometric });
    jest.spyOn(Alert, "alert").mockImplementation(() => undefined);
  });

  afterEach(() => jest.restoreAllMocks());

  it("starts the biometric login for the e-mail on mount", async () => {
    loginWithBiometric.mockResolvedValue(undefined);

    render(<BiometricPromptScreen {...props} />);
    await act(async () => {});

    expect(loginWithBiometric).toHaveBeenCalledWith("ad@sirket.com");
    expect(goBack).not.toHaveBeenCalled();
  });

  it("alerts and returns to login when the biometric login fails", async () => {
    loginWithBiometric.mockRejectedValue(new Error("cancelled"));

    render(<BiometricPromptScreen {...props} />);
    await act(async () => {});

    expect(Alert.alert).toHaveBeenCalledWith(
      "Biyometrik giriş başarısız",
      "Lütfen tekrar deneyin veya şifrenizle giriş yapın."
    );
    expect(goBack).toHaveBeenCalledTimes(1);
  });

  it("returns to login from the cancel button", async () => {
    loginWithBiometric.mockReturnValue(new Promise(() => undefined));
    const renderer = render(<BiometricPromptScreen {...props} />);

    act(() => findButton(renderer, "Vazgeç").props.onPress());

    expect(goBack).toHaveBeenCalledTimes(1);
  });
});
