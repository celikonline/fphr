import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { PunchTiles } from "@/screens/app/checkin/PunchTiles";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

const times = { in: "09:00", out: null, breakStart: null, breakEnd: null };
const available = { in: false, out: true, breakStart: true, breakEnd: false };

function tile(renderer: ReactTestRenderer, action: string) {
  return renderer.root.find(
    (node) => node.props.testID === `punch-tile-${action}` && node.props.onPress
  );
}

describe("PunchTiles", () => {
  it("labels each tile with its time or 'kayıt yok' and marks the next punch", () => {
    const renderer = renderComponent(
      <PunchTiles
        times={times}
        available={available}
        next="out"
        disabled={false}
        onPress={jest.fn()}
      />
    );

    expect(tile(renderer, "in").props.accessibilityLabel).toBe("Giriş, 09:00");
    expect(tile(renderer, "out").props.accessibilityLabel).toBe("Çıkış, kayıt yok");
    expect(tile(renderer, "out").props.accessibilityState).toEqual({
      disabled: false,
      selected: true,
    });
    expect(tile(renderer, "in").props.accessibilityState).toEqual({
      disabled: true,
      selected: false,
    });
  });

  it("punches the tapped available action", () => {
    const onPress = jest.fn();
    const renderer = renderComponent(
      <PunchTiles
        times={times}
        available={available}
        next="out"
        disabled={false}
        onPress={onPress}
      />
    );

    act(() => tile(renderer, "breakStart").props.onPress());

    expect(onPress).toHaveBeenCalledWith("breakStart");
  });

  it("disables every tile while a punch is in flight", () => {
    const renderer = renderComponent(
      <PunchTiles times={times} available={available} next="out" disabled onPress={jest.fn()} />
    );

    expect(tile(renderer, "out").props.disabled).toBe(true);
    expect(tile(renderer, "breakStart").props.disabled).toBe(true);
  });
});
