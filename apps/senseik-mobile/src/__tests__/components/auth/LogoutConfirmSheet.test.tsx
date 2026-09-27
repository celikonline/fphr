import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Modal, Text } from "react-native";
import { LogoutConfirmSheet } from "@/components/auth/LogoutConfirmSheet";
import { useAuthStore } from "@/store/authStore";

// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("@/store/authStore");

const logout = jest.fn();
const LOGOUT_FAILED = "Çıkış yapılamadı. Lütfen tekrar deneyin.";

function render(onClose = jest.fn()): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(<LogoutConfirmSheet visible onClose={onClose} />);
  });
  return renderer;
}

/** The touchable carrying the accessibility label (PrimaryButton / BottomSheet scrim). */
function findButton(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

function allTexts(renderer: ReactTestRenderer): string[] {
  return renderer.root
    .findAllByType(Text)
    .map((node) => node.props.children)
    .filter((child): child is string => typeof child === "string");
}

beforeEach(() => {
  logout.mockReset();
  (useAuthStore as unknown as jest.Mock).mockReturnValue({ logout });
});

describe("LogoutConfirmSheet", () => {
  it("asks for confirmation without logging out on open", () => {
    const renderer = render();

    expect(allTexts(renderer)).toEqual(
      expect.arrayContaining([
        "Çıkış yapılsın mı?",
        "Tekrar giriş yapmanız gerekecek.",
        "Çıkış yap",
        "Vazgeç",
      ])
    );
    expect(logout).not.toHaveBeenCalled();
  });

  it("closes without logging out on 'Vazgeç'", () => {
    const onClose = jest.fn();
    const renderer = render(onClose);

    act(() => findButton(renderer, "Vazgeç").props.onPress());

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(logout).not.toHaveBeenCalled();
  });

  it("logs out once on confirm, showing loading and ignoring repeat taps meanwhile", async () => {
    let resolveLogout: () => void = () => undefined;
    logout.mockReturnValue(new Promise<void>((resolve) => (resolveLogout = resolve)));
    const onClose = jest.fn();
    const renderer = render(onClose);

    const confirm = findButton(renderer, "Çıkış yap");
    await act(async () => {
      confirm.props.onPress();
      confirm.props.onPress();
    });

    expect(logout).toHaveBeenCalledTimes(1);
    expect(findButton(renderer, "Çıkış yap").props.accessibilityState).toEqual({
      disabled: true,
      busy: true,
    });
    expect(findButton(renderer, "Vazgeç").props.disabled).toBe(true);

    // Back button / scrim tap cannot dismiss the sheet while the logout is in flight.
    act(() => renderer.root.findByType(Modal).props.onRequestClose());
    act(() => findButton(renderer, "Kapat").props.onPress());
    expect(onClose).not.toHaveBeenCalled();

    await act(async () => resolveLogout());

    expect(logout).toHaveBeenCalledTimes(1);
    expect(allTexts(renderer)).not.toContain(LOGOUT_FAILED);
  });

  it("keeps the user signed in with an inline error when logout fails, and allows a retry", async () => {
    logout.mockRejectedValueOnce(new Error("secure store unavailable"));
    const onClose = jest.fn();
    const renderer = render(onClose);

    await act(async () => findButton(renderer, "Çıkış yap").props.onPress());

    expect(allTexts(renderer)).toContain(LOGOUT_FAILED);
    expect(onClose).not.toHaveBeenCalled();
    expect(findButton(renderer, "Çıkış yap").props.accessibilityState).toEqual({
      disabled: false,
      busy: false,
    });

    logout.mockResolvedValueOnce(undefined);
    await act(async () => findButton(renderer, "Çıkış yap").props.onPress());

    expect(logout).toHaveBeenCalledTimes(2);
    expect(allTexts(renderer)).not.toContain(LOGOUT_FAILED);
  });
});
