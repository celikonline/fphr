import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { TouchableOpacity } from "react-native";
import { ActionTile } from "@/components/ui/ActionTile";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function renderTile(badge?: number): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(
      <ActionTile
        label="Onaylarım"
        icon="checkmark-circle-outline"
        accent="blue"
        badge={badge}
        onPress={jest.fn()}
      />
    );
  });
  return renderer;
}

function tileLabel(renderer: ReactTestRenderer): unknown {
  return renderer.root.findByType(TouchableOpacity).props.accessibilityLabel;
}

describe("ActionTile", () => {
  it("announces the pending badge count to screen readers", () => {
    expect(tileLabel(renderTile(3))).toBe("Onaylarım, 3 bekliyor");
  });

  it("falls back to the plain label when the badge is zero", () => {
    expect(tileLabel(renderTile(0))).toBe("Onaylarım");
  });

  it("falls back to the plain label when there is no badge", () => {
    expect(tileLabel(renderTile(undefined))).toBe("Onaylarım");
  });
});
