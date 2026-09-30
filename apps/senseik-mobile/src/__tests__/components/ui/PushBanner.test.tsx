import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { Text } from "react-native";
import { PushBanner } from "@/components/ui/PushBanner";

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

describe("PushBanner", () => {
  it("shows the title and body text", () => {
    const renderer = renderComponent(
      <PushBanner title="Yeni bildirim" body="Detaylar için dokun" />
    );

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("Yeni bildirim");
    expect(texts).toContain("Detaylar için dokun");
  });

  it("omits an empty title or body instead of rendering an empty text node", () => {
    const renderer = renderComponent(<PushBanner title="" body="Sadece gövde" />);

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toEqual(["Sadece gövde"]);
  });
});
