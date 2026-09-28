import React from "react";
import { Text } from "react-native";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { GlassCard } from "@/components/ui";
import { AttendanceDayCard } from "@/screens/app/attendance/AttendanceDayCard";
import type { AttendanceDay } from "@/services/AttendanceService";

jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

function texts(renderer: ReactTestRenderer): string[] {
  return renderer.root
    .findAllByType(Text)
    .map((node) => node.props.children)
    .flat()
    .filter((value): value is string => typeof value === "string");
}

const baseDay: AttendanceDay = {
  id: "00000000-0000-0000-0000-000000000000",
  employeeId: "employee-1",
  date: "2026-09-22",
  plannedMinutes: 0,
  workedMinutes: 0,
  anomalies: "none",
  resolutionState: "resolved",
  source: "manual",
  allowedActions: [],
};

describe("AttendanceDayCard", () => {
  it("shows 'İzinli' with the mapped leave type name for an unmasked leave-only day", () => {
    const day: AttendanceDay = { ...baseDay, onLeave: true, leaveTypeName: "leave.type.annual" };

    const renderer = renderComponent(<AttendanceDayCard day={day} overtimeText={null} />);

    expect(texts(renderer)).toContain("İzinli");
    expect(texts(renderer)).toContain("Yıllık izin");
    expect(texts(renderer)).not.toContain("Eksik");
  });

  it("shows only 'İzinli' (no secondary text) for a masked leave type", () => {
    const day: AttendanceDay = { ...baseDay, onLeave: true, leaveTypeName: undefined };

    const renderer = renderComponent(<AttendanceDayCard day={day} overtimeText={null} />);

    expect(texts(renderer)).toContain("İzinli");
    expect(texts(renderer)).not.toContain("Eksik");
  });

  it("keeps the worked-on-leave warning distinct from a plain leave day", () => {
    const day: AttendanceDay = {
      ...baseDay,
      workedMinutes: 120,
      anomalies: "workedWhileOnLeave",
      onLeave: true,
      leaveTypeName: "leave.type.annual",
    };

    const renderer = renderComponent(<AttendanceDayCard day={day} overtimeText={null} />);

    expect(texts(renderer)).toContain("İzinli günde çalışma");
    expect(texts(renderer)).not.toContain("İzinli");
  });

  it("includes the leave type name and overtime text in the card's accessibilityLabel", () => {
    const day: AttendanceDay = { ...baseDay, onLeave: true, leaveTypeName: "leave.type.annual" };

    const renderer = renderComponent(
      <AttendanceDayCard day={day} overtimeText="Fazla mesai: 1 sa 0 dk" />
    );

    const label = renderer.root.findByType(GlassCard).props.accessibilityLabel as string;
    expect(label).toContain("Yıllık izin");
    expect(label).toContain("Fazla mesai: 1 sa 0 dk");
  });
});
