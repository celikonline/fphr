import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { Text, TouchableOpacity } from "react-native";
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
});
