import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { ActivityIndicator, Text, TouchableOpacity } from "react-native";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";
import type { LeaveRequest } from "@/services/LeaveService";

function buildRequest(overrides: Partial<LeaveRequest> = {}): LeaveRequest {
  return {
    id: "request-1",
    employeeId: "employee-1",
    leaveTypeId: "type-1",
    leaveTypeCode: "annual",
    startDate: "2026-10-01",
    endDate: "2026-10-03",
    calculatedDays: 3,
    status: "pending",
    submittedAt: "2026-09-20T10:00:00Z",
    leaveTypeName: "Yıllık izin",
    ...overrides,
  };
}

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

/**
 * The Text host component's `.children` can hold nested test-instance/element
 * wrappers rather than raw strings (depends on the RN test-renderer mock), so a
 * plain `.join("")` on them yields `[object Object]`. Flatten recursively instead.
 */
function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

describe("LeaveRequestListItem", () => {
  it("shows the leave type, date range and status label", () => {
    const renderer = renderComponent(<LeaveRequestListItem request={buildRequest()} />);

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("Yıllık izin");
    expect(texts).toContain("Onay bekliyor");
    expect(texts.some((text) => text.includes("3 gün"))).toBe(true);
  });

  it("formats a decimal day count with the Turkish decimal comma (B2)", () => {
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest({ calculatedDays: 2.5 })} />
    );

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts.some((text) => text.includes("2,5 gün"))).toBe(true);
  });

  it("falls back to the leave type code when no display name is present", () => {
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest({ leaveTypeName: null })} />
    );

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("annual");
  });

  it("is not pressable when no onPress handler is given", () => {
    const renderer = renderComponent(<LeaveRequestListItem request={buildRequest()} />);

    expect(renderer.root.findAllByType(TouchableOpacity)).toHaveLength(0);
  });

  it("calls onPress when the row is tapped", () => {
    const onPress = jest.fn();
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest()} onPress={onPress} />
    );

    act(() => {
      renderer.root.findByType(TouchableOpacity).props.onPress();
    });

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  describe("Geri çek (H1: withdraw a pending request)", () => {
    it("shows a Geri çek button for a pending request when onWithdraw is given", () => {
      const onWithdraw = jest.fn();
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "pending" })}
          onWithdraw={onWithdraw}
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).toContain("Geri çek");

      act(() => {
        renderer.root.findByType(TouchableOpacity).props.onPress();
      });
      expect(onWithdraw).toHaveBeenCalledTimes(1);
    });

    it("does not show a Geri çek button when onWithdraw is not given (e.g. ProfileScreen's summary usage)", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest({ status: "pending" })} />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
    });

    it("does not show a Geri çek button for a non-pending request even when onWithdraw is given", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "approved" })}
          onWithdraw={jest.fn()}
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
    });

    it("shows a loading indicator instead of the label while withdrawing, and disables the button", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "pending" })}
          onWithdraw={jest.fn()}
          isWithdrawing
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
      expect(renderer.root.findAllByType(ActivityIndicator)).toHaveLength(1);
      expect(renderer.root.findByType(TouchableOpacity).props.disabled).toBe(true);
    });
  });
});
