import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Text, TouchableOpacity } from "react-native";
import LoginScreen from "@/screens/auth/LoginScreen";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";
import type { AuthStackParamList } from "@/types/navigation";
import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import type { TenantChoice } from "@/types";
import i18n from "@/lib/i18n";

// react-native-reanimated v4's shipped `/mock` entry pulls in `react-native-worklets`'
// native module bootstrap and throws outside a real app runtime, so it is unusable under
// jest-expo as-is. Stub the handful of APIs LoginScreen/AuthBackdrop/BrandMark use instead -
// entering/withX animations resolve synchronously so component behavior stays testable.
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
jest.mock("@/store/authStore");
jest.mock("@/lib/biometric");

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

const login = jest.fn();
const clearTenantChoices = jest.fn();
const completeMfa = jest.fn();
const clearMfaChallenge = jest.fn();

function mockStore(overrides: Partial<ReturnType<typeof useAuthStore>> = {}) {
  (useAuthStore as unknown as jest.Mock).mockReturnValue({
    login,
    completeMfa,
    clearMfaChallenge,
    isLoading: false,
    tenantChoices: null,
    clearTenantChoices,
    mfaToken: null,
    ...overrides,
  });
}

function buildProps(): Props {
  return {
    navigation: { navigate: jest.fn() },
    route: { key: "login", name: "Login" },
  } as unknown as Props;
}

/** Flattens a Text node's children (which can be nested test-instance wrappers) into a string. */
function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

function allTexts(renderer: ReactTestRenderer): string[] {
  return renderer.root.findAllByType(Text).map((node) => extractText(node.children));
}

/**
 * The submit button is a `Pressable` from `react-native`, but under this project's pnpm layout the
 * package can resolve to two distinct module instances (one for the app, one transitively pulled in
 * by a dependency), so `findByType(Pressable)` misses it even though the same component renders.
 * Querying by displayName sidesteps that reference mismatch.
 */
function findSubmitButton(renderer: ReactTestRenderer): ReactTestInstance {
  return renderer.root.find((node) => {
    if (typeof node.type === "string") return false;
    const type = node.type as { displayName?: string; name?: string };
    return type.displayName === "Pressable" || type.name === "Pressable";
  });
}

async function renderScreen(props: Props = buildProps()): Promise<ReactTestRenderer> {
  let renderer!: ReactTestRenderer;
  await act(async () => {
    renderer = create(<LoginScreen {...props} />);
  });
  return renderer;
}

describe("LoginScreen", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockStore();
    (biometricService.isBiometricAvailable as jest.Mock).mockResolvedValue({
      available: false,
      enrolled: false,
      supportedTypes: [],
    });
  });

  it("does not call login and shows the validation message when fields are empty", async () => {
    const renderer = await renderScreen();

    await act(async () => {
      findSubmitButton(renderer).props.onPress();
    });

    expect(login).not.toHaveBeenCalled();
    expect(allTexts(renderer)).toContain("Lütfen e-posta ve şifrenizi girin");
  });

  it("calls login with the entered credentials and no tenant slug", async () => {
    login.mockResolvedValue({ status: "ok" });
    const renderer = await renderScreen();

    const emailInput = renderer.root.findAll(
      (node) => node.props.placeholder === "ad.soyad@sirket.com"
    )[0];
    const passwordInput = renderer.root.findAll(
      (node) => node.props.placeholder === "Şifrenizi girin"
    )[0];

    await act(async () => {
      emailInput.props.onChangeText("ayse@ornek.com");
      passwordInput.props.onChangeText("gizli-sifre");
    });

    await act(async () => {
      findSubmitButton(renderer).props.onPress();
    });

    expect(login).toHaveBeenCalledWith("ayse@ornek.com", "gizli-sifre", undefined);
  });

  it("shows the generic message when login rejects with a plain error", async () => {
    login.mockRejectedValue(new Error("invalid_credentials"));
    const renderer = await renderScreen();

    const emailInput = renderer.root.findAll(
      (node) => node.props.placeholder === "ad.soyad@sirket.com"
    )[0];
    const passwordInput = renderer.root.findAll(
      (node) => node.props.placeholder === "Şifrenizi girin"
    )[0];

    await act(async () => {
      emailInput.props.onChangeText("ayse@ornek.com");
      passwordInput.props.onChangeText("gizli-sifre");
    });

    await act(async () => {
      findSubmitButton(renderer).props.onPress();
    });

    expect(allTexts(renderer)).toContain("E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
  });

  it("switches to the code-entry step when login resolves mfaRequired, without an error banner", async () => {
    login.mockResolvedValue({ status: "mfaRequired", mfaToken: "mfa-token-abc" });
    const props = buildProps();
    const renderer = await renderScreen(props);

    const emailInput = renderer.root.findAll(
      (node) => node.props.placeholder === "ad.soyad@sirket.com"
    )[0];
    const passwordInput = renderer.root.findAll(
      (node) => node.props.placeholder === "Şifrenizi girin"
    )[0];

    await act(async () => {
      emailInput.props.onChangeText("ayse@ornek.com");
      passwordInput.props.onChangeText("gizli-sifre");
    });

    await act(async () => {
      findSubmitButton(renderer).props.onPress();
    });

    // The real store would have set `mfaToken` by now - the mock re-renders the screen with it.
    mockStore({ mfaToken: "mfa-token-abc" });
    await act(async () => {
      renderer.update(<LoginScreen {...props} />);
    });

    expect(allTexts(renderer)).toContain("İki adımlı doğrulama");
    expect(allTexts(renderer)).not.toContain("E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
  });

  describe("MFA code step", () => {
    function findCodeInput(renderer: ReactTestRenderer): ReactTestInstance {
      return renderer.root.findAll((node) => node.props.placeholder === "000000")[0];
    }

    it("calls completeMfa with the entered 6-digit code", async () => {
      completeMfa.mockResolvedValue(undefined);
      mockStore({ mfaToken: "mfa-token-abc" });
      const renderer = await renderScreen();

      await act(async () => {
        findCodeInput(renderer).props.onChangeText("123456");
      });
      await act(async () => {
        findSubmitButton(renderer).props.onPress();
      });

      expect(completeMfa).toHaveBeenCalledWith("123456");
    });

    it("strips non-digit characters and caps the code at 6 digits", async () => {
      mockStore({ mfaToken: "mfa-token-abc" });
      const renderer = await renderScreen();

      await act(async () => {
        findCodeInput(renderer).props.onChangeText("12-34ab56789");
      });

      expect(findCodeInput(renderer).props.value).toBe("123456");
    });

    it("shows a generic message and does not clear the challenge on an invalid code", async () => {
      completeMfa.mockRejectedValue(
        Object.assign(new Error("invalid"), {
          isAxiosError: true,
          response: { data: { code: "identity.mfa.invalid_code" } },
        })
      );
      mockStore({ mfaToken: "mfa-token-abc" });
      const renderer = await renderScreen();

      await act(async () => {
        findCodeInput(renderer).props.onChangeText("000000");
      });
      await act(async () => {
        findSubmitButton(renderer).props.onPress();
      });

      expect(allTexts(renderer)).toContain("Doğrulama kodu hatalı. Lütfen tekrar deneyin.");
      expect(clearMfaChallenge).not.toHaveBeenCalled();
      expect(findCodeInput(renderer).props.value).toBe("");
    });

    it("clears the challenge and returns to the login form on an expired token", async () => {
      completeMfa.mockRejectedValue(
        Object.assign(new Error("expired"), {
          isAxiosError: true,
          response: { data: { code: "identity.mfa.token_expired" } },
        })
      );
      const props = buildProps();
      mockStore({ mfaToken: "mfa-token-abc" });
      const renderer = await renderScreen(props);

      await act(async () => {
        findCodeInput(renderer).props.onChangeText("123456");
      });
      await act(async () => {
        findSubmitButton(renderer).props.onPress();
      });

      expect(clearMfaChallenge).toHaveBeenCalled();

      // The real store would have cleared `mfaToken` by now.
      mockStore({ mfaToken: null });
      await act(async () => {
        renderer.update(<LoginScreen {...props} />);
      });

      expect(allTexts(renderer)).toContain("Doğrulama süresi doldu. Lütfen tekrar giriş yapın.");
      expect(allTexts(renderer)).toContain("Tekrar hoş geldiniz");
    });

    it("cancels the challenge when 'Farklı hesapla giriş yap' is tapped", async () => {
      mockStore({ mfaToken: "mfa-token-abc" });
      const renderer = await renderScreen();

      const cancelLink = renderer.root
        .findAllByType(TouchableOpacity)
        .find((instance) =>
          instance
            .findAllByType(Text)
            .some((node) => extractText(node.children) === "Farklı hesapla giriş yap")
        );

      await act(async () => {
        cancelLink?.props.onPress();
      });

      expect(clearMfaChallenge).toHaveBeenCalled();
    });
  });

  it("lists tenant choices and logs in with the selected slug when tapped", async () => {
    // Mirrors the real flow: the user fills the form and submits (login resolves "select_tenant"),
    // then the same mounted screen re-renders with `tenantChoices` set - local email/password state
    // survives because the component never unmounts.
    const tenants: TenantChoice[] = [
      { tenantId: "tenant-1", slug: "acme", name: "Acme A.Ş." },
      { tenantId: "tenant-2", slug: "beta", name: "Beta Ltd." },
    ];
    const props = buildProps();
    const renderer = await renderScreen(props);

    const emailInput = renderer.root.findAll(
      (node) => node.props.placeholder === "ad.soyad@sirket.com"
    )[0];
    const passwordInput = renderer.root.findAll(
      (node) => node.props.placeholder === "Şifrenizi girin"
    )[0];

    await act(async () => {
      emailInput.props.onChangeText("ayse@ornek.com");
      passwordInput.props.onChangeText("gizli-sifre");
    });

    mockStore({ tenantChoices: tenants });
    await act(async () => {
      renderer.update(<LoginScreen {...props} />);
    });

    expect(allTexts(renderer)).toEqual(expect.arrayContaining(["Acme A.Ş.", "Beta Ltd."]));

    const acmeRow = renderer.root
      .findAllByType(TouchableOpacity)
      .find((instance) =>
        instance.findAllByType(Text).some((node) => extractText(node.children) === "Acme A.Ş.")
      );

    await act(async () => {
      acmeRow?.props.onPress();
    });

    expect(login).toHaveBeenCalledWith("ayse@ornek.com", "gizli-sifre", "acme");
  });
});

describe("LoginScreen in English (FG-001b)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockStore();
    (biometricService.isBiometricAvailable as jest.Mock).mockResolvedValue({
      available: false,
      enrolled: false,
      supportedTypes: [],
    });
  });

  it("renders the form and its validation message in the active language, same flow", async () => {
    await act(async () => {
      await i18n.changeLanguage("en");
    });
    const renderer = await renderScreen();

    expect(allTexts(renderer)).toEqual(
      expect.arrayContaining(["Welcome back", "Forgot password", "EMAIL", "PASSWORD"])
    );

    await act(async () => {
      findSubmitButton(renderer).props.onPress();
    });

    expect(login).not.toHaveBeenCalled();
    expect(allTexts(renderer)).toContain("Please enter your email and password");
    expect(allTexts(renderer)).not.toContain("Lütfen e-posta ve şifrenizi girin");
  });
});
