import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { KeyboardAvoidingView, Modal, Platform, Text } from "react-native";
import { BottomSheet } from "@/components/ui/BottomSheet";

function renderSheet(onClose = jest.fn()): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(
      <BottomSheet visible onClose={onClose} title="Talebi reddet">
        <Text>Gerekçe</Text>
      </BottomSheet>
    );
  });
  return renderer;
}

afterEach(() => {
  jest.restoreAllMocks();
});

describe("BottomSheet", () => {
  it("lifts its content above the iOS keyboard with padding", () => {
    jest.replaceProperty(Platform, "OS", "ios");
    const avoider = renderSheet().root.findByType(KeyboardAvoidingView);
    expect(avoider.props.behavior).toBe("padding");
    expect(avoider.findAllByType(Text).some((node) => node.props.children === "Gerekçe")).toBe(
      true
    );
  });

  it("leaves Android to the window's own resize (no avoiding behaviour)", () => {
    jest.replaceProperty(Platform, "OS", "android");
    expect(renderSheet().root.findByType(KeyboardAvoidingView).props.behavior).toBeUndefined();
  });

  it("closes on the Android back button and on a scrim tap", () => {
    const onClose = jest.fn();
    const root = renderSheet(onClose).root;
    act(() => root.findByType(Modal).props.onRequestClose());
    act(() => root.findByProps({ accessibilityLabel: "Kapat" }).props.onPress());
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
