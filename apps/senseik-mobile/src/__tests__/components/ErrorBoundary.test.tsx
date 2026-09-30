import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { Text, TouchableOpacity } from "react-native";
import { ErrorBoundary } from "@/components/ErrorBoundary";

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

function texts(renderer: ReactTestRenderer): string[] {
  return renderer.root.findAllByType(Text).map((node) => extractText(node.children));
}

/** Throws on render while `shouldThrow` is true; used to force the boundary's error path. */
function Bomb({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) {
    throw new Error("boom");
  }
  return <Text>Çocuk içerik</Text>;
}

describe("ErrorBoundary", () => {
  let consoleErrorSpy: jest.SpyInstance;

  beforeEach(() => {
    // React (and our own componentDidCatch dev log) both write to console.error when a child
    // throws during render - expected noise for these tests, not a real failure.
    consoleErrorSpy = jest.spyOn(console, "error").mockImplementation(() => undefined);
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
  });

  it("renders the child when there is no error", () => {
    const renderer = renderComponent(
      <ErrorBoundary>
        <Bomb shouldThrow={false} />
      </ErrorBoundary>
    );

    expect(texts(renderer)).toContain("Çocuk içerik");
  });

  it("shows the fallback UI when a child throws during render", () => {
    const renderer = renderComponent(
      <ErrorBoundary>
        <Bomb shouldThrow />
      </ErrorBoundary>
    );

    expect(texts(renderer)).toContain("Bir şeyler ters gitti");
    expect(texts(renderer)).toContain("Beklenmeyen bir hata oluştu. Yeniden deneyebilirsiniz.");
  });

  it("re-renders the child after 'Yeniden dene' when it no longer throws", () => {
    const renderer = renderComponent(
      <ErrorBoundary>
        <Bomb shouldThrow />
      </ErrorBoundary>
    );

    expect(texts(renderer)).toContain("Bir şeyler ters gitti");

    // While the fallback is showing, the boundary still holds `hasError: true` and ignores its
    // `children` prop - updating it here only refreshes what will render once the error clears,
    // it does not re-trigger the throw.
    act(() => {
      renderer.update(
        <ErrorBoundary>
          <Bomb shouldThrow={false} />
        </ErrorBoundary>
      );
    });

    const retryButton = renderer.root.findByType(TouchableOpacity);
    act(() => {
      retryButton.props.onPress();
    });

    expect(texts(renderer)).toContain("Çocuk içerik");
    expect(texts(renderer)).not.toContain("Bir şeyler ters gitti");
  });
});
