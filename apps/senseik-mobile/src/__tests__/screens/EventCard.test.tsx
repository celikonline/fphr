import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { Text } from "react-native";
import { EventCard } from "@/screens/app/home/EventCard";
import type { CompanyCalendarEntry } from "@/services/HomeFeedService";
import { accents } from "@/theme/theme";

// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function render(event: Partial<CompanyCalendarEntry>): ReactTestRenderer {
  const entry = {
    type: "companyEvent",
    title: "Yıl sonu buluşması",
    date: "2026-12-18",
    ...event,
  } as CompanyCalendarEntry;
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(<EventCard event={entry} />);
  });
  return renderer;
}

function icon(renderer: ReactTestRenderer) {
  return renderer.root.findAll((node) => typeof node.props.name === "string")[0].props;
}

describe("EventCard", () => {
  it("shows a people icon in the purple accent for a meeting", () => {
    const props = icon(render({ type: "meeting" }));
    expect(props.name).toBe("people-outline");
    expect(props.color).toBe(accents.purple.fg);
  });

  it("shows a calendar icon in the blue accent for a company event, with no emoji text", () => {
    const renderer = render({ type: "companyEvent" });
    expect(icon(renderer).name).toBe("calendar-outline");
    expect(icon(renderer).color).toBe(accents.blue.fg);
    const texts = renderer.root.findAllByType(Text).map((node) => node.props.children);
    expect(texts.flat().join(" ")).not.toMatch(/👥|📋/u);
  });
});
