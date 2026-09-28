import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { GlassCard } from "@/components/ui";
import { RequestCardView } from "@/screens/app/requests/RequestCardView";
import type { RequestCard } from "@/screens/app/requests/requestCards";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

const card: RequestCard = {
  key: "leave-1",
  id: "leave-1",
  kind: "leave",
  title: "Yıllık izin",
  dateLabel: "29 Eyl → 3 Eki",
  detail: "4 gün",
  statusLabel: "Onay bekliyor",
  tone: "warning",
  description: null,
  lines: [],
  canWithdraw: false,
  sortDate: "2026-09-20",
  sortInstant: 0,
};

describe("RequestCardView (FG-009: Taleplerim kartından detaya geçiş)", () => {
  it("is not pressable when onPress is omitted (no approvalRequestId resolved yet)", () => {
    const renderer = renderComponent(
      <RequestCardView card={card} isWithdrawing={false} onWithdraw={jest.fn()} />
    );

    expect(renderer.root.findByType(GlassCard).props.onPress).toBeUndefined();
  });

  it("becomes pressable and calls onPress when a mapping is given", () => {
    const onPress = jest.fn();
    const renderer = renderComponent(
      <RequestCardView card={card} isWithdrawing={false} onWithdraw={jest.fn()} onPress={onPress} />
    );

    const glassCard = renderer.root.findByType(GlassCard);
    expect(glassCard.props.accessibilityLabel).toBe(card.title);
    act(() => {
      glassCard.props.onPress();
    });
    expect(onPress).toHaveBeenCalledTimes(1);
  });
});
