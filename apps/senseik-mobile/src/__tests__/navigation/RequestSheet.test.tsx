import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Text } from "react-native";
import { RequestSheet } from "@/navigation/RequestSheet";

// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

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

describe("RequestSheet", () => {
  it("lists the four request types under the sheet title", () => {
    const renderer = render(<RequestSheet visible onClose={jest.fn()} onSelect={jest.fn()} />);

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toEqual(
      expect.arrayContaining([
        "Yeni talep",
        "Ne için talep oluşturmak istiyorsun?",
        "İzin",
        "Fazla mesai",
        "Masraf",
        "Avans",
      ])
    );
  });

  it("reports the picked request kind", () => {
    const onSelect = jest.fn();
    const renderer = render(<RequestSheet visible onClose={jest.fn()} onSelect={onSelect} />);

    act(() => findButton(renderer, "Masraf").props.onPress());

    expect(onSelect).toHaveBeenCalledWith("expense");
  });
});
