import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { Text } from "react-native";
import { StatusPill } from "@/components/ui/StatusPill";

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

describe("StatusPill", () => {
  it("shows the label text", () => {
    const renderer = renderComponent(<StatusPill label="Bekliyor" tone="warning" />);

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("Bekliyor");
  });
});
