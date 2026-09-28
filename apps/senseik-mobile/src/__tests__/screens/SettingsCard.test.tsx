import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Text, View } from "react-native";
import AsyncStorage from "@react-native-async-storage/async-storage";
import i18n from "@/lib/i18n";
import { SettingsCard } from "@/screens/app/profile/SettingsCard";
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

function texts(renderer: ReactTestRenderer): string[] {
  return renderer.root.findAllByType(Text).map((node) => extractText(node.children));
}

function findButton(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

/** Waits for `changeAppLanguage`'s async persist + the resulting re-render. */
async function flush(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

describe("Profil > Ayarlar > Dil (FG-001)", () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  afterEach(async () => {
    await act(async () => {
      await i18n.changeLanguage("tr");
    });
  });

  it("shows the current language on the Dil row", () => {
    let renderer!: ReactTestRenderer;
    act(() => {
      renderer = create(<SettingsCard />);
    });

    expect(texts(renderer)).toEqual(expect.arrayContaining(["AYARLAR", "Dil", "Türkçe"]));
  });

  it("lists every language in its own name and switches the app without a restart", async () => {
    let renderer!: ReactTestRenderer;
    act(() => {
      renderer = create(
        <View>
          <SettingsCard />
          {/* Another already-mounted screen's component must follow the switch too. */}
          <RequestSheet visible onClose={jest.fn()} onSelect={jest.fn()} />
        </View>
      );
    });

    act(() => findButton(renderer, "Dil: Türkçe").props.onPress());
    expect(texts(renderer)).toEqual(expect.arrayContaining(["Türkçe", "English"]));

    act(() => findButton(renderer, "English").props.onPress());
    await flush();

    expect(i18n.language).toBe("en");
    expect(await AsyncStorage.getItem("language")).toBe("en");
    expect(texts(renderer)).toEqual(
      expect.arrayContaining(["SETTINGS", "Language", "English", "New request", "Leave"])
    );
    expect(texts(renderer)).not.toContain("Yeni talep");
  });
});
