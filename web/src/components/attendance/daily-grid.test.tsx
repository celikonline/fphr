import { beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trDaily from "../../../public/locales/tr/attendance-daily.json";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trLeave from "../../../public/locales/tr/leave.json";
import DailyGrid from "./daily-grid";
import type { DailyAttendanceRow } from "@/services/attendance-daily.service";

beforeAll(async () => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false, media: query, onchange: null,
      addListener: () => {}, removeListener: () => {},
      addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  await i18next.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["attendance-daily", "attendance", "leave"],
    resources: { tr: { "attendance-daily": trDaily, attendance: trAttendance, leave: trLeave } },
    interpolation: { escapeValue: false },
  });
});

const base: DailyAttendanceRow = {
  employeeId: "e1",
  employeeName: "Ayşe Yılmaz",
  date: "2026-09-25",
  workedMinutes: 0,
  overtimeMinutes: 0,
  anomalies: [],
};

function renderGrid(rows: DailyAttendanceRow[]) {
  return render(
    <I18nextProvider i18n={i18next}>
      <MantineProvider>
        <DailyGrid rows={rows} canManage={false} onAddPunch={vi.fn()} onDeletePunch={vi.fn()} />
      </MantineProvider>
    </I18nextProvider>,
  );
}

describe("DailyGrid", () => {
  it("hides the unit column when no row on the page carries a unit name", () => {
    renderGrid([base, { ...base, employeeId: "e2", employeeName: "Can Demir", orgUnitName: "" }]);
    expect(screen.queryByRole("columnheader", { name: trDaily.dailyList.unit })).toBeNull();
  });

  it("shows the unit column when at least one row has a unit name", () => {
    renderGrid([base, { ...base, employeeId: "e2", employeeName: "Can Demir", orgUnitName: "Muhasebe" }]);
    expect(screen.getByRole("columnheader", { name: trDaily.dailyList.unit })).toBeInTheDocument();
    expect(screen.getByText("Muhasebe")).toBeInTheDocument();
  });

  it("shows a translated hint next to the incalculable label, raw text for unknown reasons", () => {
    renderGrid([
      { ...base, incalculableReason: "channelConflict" },
      { ...base, employeeId: "e2", employeeName: "Can Demir", incalculableReason: "someNewReason" },
    ]);
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]).getByText(trAttendance.incalculableReason.label)).toBeInTheDocument();
    expect(within(rows[1]).getByText(trAttendance.incalculableReason.channelConflict)).toBeInTheDocument();
    expect(within(rows[2]).getByText("someNewReason")).toBeInTheDocument();
  });
});
