import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { ActivityIndicator, Text } from "react-native";
import { RootNavigator, shouldShowRootSplash } from "@/navigation/RootNavigator";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";

// Same stub as LoginScreen.test.tsx - react-native-reanimated v4's real module throws outside a
// native runtime, and RootNavigator pulls it in transitively via LoginScreen.
jest.mock("react-native-reanimated", () => {
  const { View, Text: RNText } = jest.requireActual("react-native");

  function chainableTiming() {
    const timing: Record<string, unknown> = {};
    timing.delay = () => chainableTiming();
    timing.duration = () => chainableTiming();
    return timing;
  }

  return {
    __esModule: true,
    default: { View, Text: RNText },
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

type MockState = Record<string, unknown>;

let mockState: MockState;

function applyMockState(patch: MockState) {
  mockState = { ...mockState, ...patch };
  (useAuthStore as unknown as jest.Mock).mockReturnValue(mockState);
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

function findEmailInput(renderer: ReactTestRenderer): ReactTestInstance | undefined {
  return renderer.root.findAll((node) => node.props.placeholder === "ad.soyad@sirket.com")[0];
}

function findPasswordInput(renderer: ReactTestRenderer): ReactTestInstance | undefined {
  return renderer.root.findAll((node) => node.props.placeholder === "Şifrenizi girin")[0];
}

/** Mirrors LoginScreen.test.tsx: `Pressable` can resolve to two module instances under this
 * project's pnpm layout, so match by displayName instead of `findByType`. */
function findSubmitButton(renderer: ReactTestRenderer): ReactTestInstance | undefined {
  return renderer.root.find((node) => {
    if (typeof node.type === "string") return false;
    const type = node.type as { displayName?: string; name?: string };
    return type.displayName === "Pressable" || type.name === "Pressable";
  });
}

// Tracked so `afterEach` can unmount it - `NavigationContainer` sets up listeners/timers that
// otherwise leak across tests and leave a worker process unable to exit cleanly.
let currentRenderer: ReactTestRenderer | undefined;

async function renderRoot(): Promise<ReactTestRenderer> {
  let renderer!: ReactTestRenderer;
  await act(async () => {
    renderer = create(<RootNavigator />);
  });
  currentRenderer = renderer;
  return renderer;
}

describe("shouldShowRootSplash", () => {
  it("shows the splash before the initial checkAuth has finished", () => {
    expect(shouldShowRootSplash(false)).toBe(true);
  });

  it("does not show the splash once the app is ready, regardless of later isLoading flips", () => {
    // No `isLoading` parameter at all: login/completeMfa/logout/etc. setting `isLoading` can no
    // longer feed into this decision - that was the B14/B15 root cause (RootNavigator unmounting
    // LoginScreen mid-submit and wiping its local state).
    expect(shouldShowRootSplash(true)).toBe(false);
  });
});

describe("RootNavigator", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockState = {
      isAuthenticated: false,
      isLoading: false,
      checkAuth: jest.fn().mockResolvedValue(undefined),
      login: jest.fn(),
      completeMfa: jest.fn(),
      clearMfaChallenge: jest.fn(),
      tenantChoices: null,
      clearTenantChoices: jest.fn(),
      mfaToken: null,
    };
    (useAuthStore as unknown as jest.Mock).mockReturnValue(mockState);
    (biometricService.isBiometricAvailable as jest.Mock).mockResolvedValue({
      available: false,
      enrolled: false,
      supportedTypes: [],
    });
  });

  afterEach(() => {
    currentRenderer?.unmount();
    currentRenderer = undefined;
  });

  it("shows the splash until the initial checkAuth resolves, then renders the login form", async () => {
    let resolveCheckAuth: () => void = () => {};
    applyMockState({
      checkAuth: jest.fn(() => new Promise<void>((resolve) => (resolveCheckAuth = resolve))),
    });

    let renderer!: ReactTestRenderer;
    await act(async () => {
      renderer = create(<RootNavigator />);
    });
    currentRenderer = renderer;

    // checkAuth hasn't resolved yet: splash only, no login form.
    expect(renderer.root.findAllByType(ActivityIndicator)).toHaveLength(1);
    expect(findEmailInput(renderer)).toBeUndefined();

    await act(async () => {
      resolveCheckAuth();
    });

    expect(findEmailInput(renderer)).toBeDefined();
  });

  it("keeps the login form (and its typed email) mounted when isLoading becomes true", async () => {
    const renderer = await renderRoot();

    await act(async () => {
      findEmailInput(renderer)?.props.onChangeText("ayse@ornek.com");
    });
    expect(findEmailInput(renderer)?.props.value).toBe("ayse@ornek.com");

    // Mirrors what the real store does the instant `login`/`completeMfa` start: flip `isLoading`
    // before the network call even settles.
    applyMockState({ isLoading: true });
    await act(async () => {
      renderer.update(<RootNavigator />);
    });

    // The root splash must NOT replace the navigator mid-request - if it did, LoginScreen would
    // unmount and remount blank (B14/B15), losing the typed email below.
    expect(renderer.root.findAllByType(ActivityIndicator)).toHaveLength(0);
    expect(findEmailInput(renderer)?.props.value).toBe("ayse@ornek.com");
  });

  it("does not show the splash while a login request is in flight, and shows the inline error once it fails - without wiping the typed email", async () => {
    let rejectLogin: (reason: unknown) => void = () => {};
    const login = jest.fn(() => new Promise((_resolve, reject) => (rejectLogin = reject)));
    applyMockState({ login });
    const renderer = await renderRoot();

    await act(async () => {
      findEmailInput(renderer)?.props.onChangeText("ayse@ornek.com");
      findPasswordInput(renderer)?.props.onChangeText("wrong-pass");
    });

    // Starts the (still-pending) login request.
    act(() => {
      findSubmitButton(renderer)?.props.onPress();
    });

    // The store flips `isLoading: true` synchronously before the network call resolves.
    applyMockState({ isLoading: true });
    await act(async () => {
      renderer.update(<RootNavigator />);
    });
    expect(renderer.root.findAllByType(ActivityIndicator)).toHaveLength(0);
    expect(findEmailInput(renderer)?.props.value).toBe("ayse@ornek.com");

    // The request fails - the store's `catch` flips `isLoading` back to `false` in the same tick it
    // rejects the promise LoginScreen is awaiting.
    applyMockState({ isLoading: false });
    await act(async () => {
      rejectLogin(new Error("invalid"));
    });

    expect(findEmailInput(renderer)?.props.value).toBe("ayse@ornek.com");
    expect(allTexts(renderer)).toContain("E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
  });
});
