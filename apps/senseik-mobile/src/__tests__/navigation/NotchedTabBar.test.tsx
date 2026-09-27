import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Modal, Text } from "react-native";
import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { NotchedTabBar } from "@/navigation/NotchedTabBar";
import { palette } from "@/theme/theme";

// react-native-reanimated v4's real module throws outside a native runtime (see RootNavigator.test).
jest.mock("react-native-reanimated", () => {
  const { View } = jest.requireActual("react-native");
  return {
    __esModule: true,
    default: { View },
    useSharedValue: (initial: unknown) => ({ value: initial }),
    useAnimatedStyle: (factory: () => Record<string, unknown>) => factory(),
    useReducedMotion: () => false,
    withSpring: (toValue: unknown) => toValue,
  };
});
// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("react-native-safe-area-context", () => ({
  useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 20, left: 0 }),
}));

function renderBar(focusedIndex = 0) {
  const navigate = jest.fn();
  const emit = jest.fn(() => ({ defaultPrevented: false }));
  const props = {
    state: {
      index: focusedIndex,
      routes: [
        { key: "Home-key", name: "Home" },
        { key: "CheckIn-key", name: "CheckIn" },
      ],
    },
    navigation: { navigate, emit },
    descriptors: {},
    insets: { top: 0, right: 0, bottom: 20, left: 0 },
  } as unknown as BottomTabBarProps;

  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(<NotchedTabBar {...props} />);
  });
  return { renderer, navigate, emit };
}

function findButton(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

function sheetVisible(renderer: ReactTestRenderer): boolean {
  return renderer.root.findByType(Modal).props.visible;
}

describe("NotchedTabBar", () => {
  it("switches to an unfocused tab after emitting tabPress", () => {
    const { renderer, navigate, emit } = renderBar(0);

    act(() => findButton(renderer, "Giriş / Çıkış").props.onPress());

    expect(emit).toHaveBeenCalledWith({
      type: "tabPress",
      target: "CheckIn-key",
      canPreventDefault: true,
    });
    expect(navigate).toHaveBeenCalledWith("CheckIn", undefined);
  });

  it("does not re-navigate when the focused tab is pressed", () => {
    const { renderer, navigate } = renderBar(0);

    act(() => findButton(renderer, "Ana sayfa").props.onPress());

    expect(navigate).not.toHaveBeenCalled();
  });

  it("marks only the focused tab as selected", () => {
    const { renderer } = renderBar(1);

    expect(findButton(renderer, "Ana sayfa").props.accessibilityState).toEqual({ selected: false });
    expect(findButton(renderer, "Giriş / Çıkış").props.accessibilityState).toEqual({
      selected: true,
    });
  });

  it("opens the request sheet from the FAB and routes the pick to NewRequest", () => {
    const { renderer, navigate } = renderBar(0);
    expect(sheetVisible(renderer)).toBe(false);

    act(() => findButton(renderer, "Yeni talep oluştur").props.onPress());
    expect(sheetVisible(renderer)).toBe(true);
    expect(findButton(renderer, "Yeni talep oluştur").props.accessibilityState).toEqual({
      expanded: true,
    });

    act(() => findButton(renderer, "Fazla mesai").props.onPress());

    expect(navigate).toHaveBeenCalledWith("NewRequest", { kind: "overtime" });
    expect(sheetVisible(renderer)).toBe(false);
  });

  it("renders the unfocused tab label in textMuted for WCAG AA contrast", () => {
    const { renderer } = renderBar(0);

    const label = findButton(renderer, "Giriş / Çıkış").findByType(Text);
    const style = [label.props.style].flat();

    expect(style).toContainEqual(expect.objectContaining({ color: palette.textMuted }));
  });
});
