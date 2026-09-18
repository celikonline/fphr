import { beforeAll, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import { TeamOverlapCalendar } from "./team-overlap-calendar";
import type { LeaveTeamOverlap } from "@/services/leave.service";

// jsdom has no matchMedia/ResizeObserver; Mantine components mounted under MantineProvider need one of these.
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "leave",
    ns: ["leave"],
    resources: { tr: { leave: trLeave } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderCalendar(overlap: LeaveTeamOverlap, requestStart = "2026-09-17", requestEnd = "2026-09-18") {
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <TeamOverlapCalendar overlap={overlap} requestStart={requestStart} requestEnd={requestEnd} locale="tr-TR" />
      </MantineProvider>
    </I18nextProvider>,
  );
}

function employee(id: string, name: string, startDate: string, endDate: string) {
  return { employeeId: id, name, startDate, endDate };
}

describe("TeamOverlapCalendar (senseik-026)", () => {
  it("shows the overlap count and org unit name as a heading", () => {
    renderCalendar({
      count: 1,
      employees: [employee("e1", "Ada Lovelace", "2026-09-17", "2026-09-18")],
      orgUnitName: "Human Resources",
    });

    expect(screen.getByText("Çakışan izinler (1)")).toBeTruthy();
    expect(screen.getByText("Human Resources")).toBeTruthy();
  });

  it("renders a day column for the request range plus context days on each side", () => {
    // Request: 2026-09-17 to 2026-09-18 (2 days) + 3 context days each side = 8 columns.
    renderCalendar({ count: 1, employees: [employee("e1", "Ada Lovelace", "2026-09-17", "2026-09-18")], orgUnitName: null });

    // Sep 14 (3 days before the 17th) through Sep 21 (3 days after the 18th).
    for (const day of ["14", "15", "16", "17", "18", "19", "20", "21"]) {
      expect(screen.getAllByText(day, { exact: true }).length).toBeGreaterThan(0);
    }
  });

  it("renders a colored bar segment for each day of an employee's leave, clipped to the visible window", () => {
    // Window is 2026-09-14..21 (request 17-18 +/- 3 context days); leave runs 16-19, so all 4 days are visible.
    renderCalendar({
      count: 1,
      employees: [employee("e1", "Ada Lovelace", "2026-09-16", "2026-09-19")],
      orgUnitName: null,
    });

    const bars = screen.getAllByTestId("overlap-bar-e1");
    expect(bars).toHaveLength(4);
  });

  it("clips a bar that extends past the visible window at either edge", () => {
    // Leave starts before and ends after the window (2026-09-14..21) - only the 8 visible days get a segment.
    renderCalendar({
      count: 1,
      employees: [employee("e1", "Ada Lovelace", "2026-08-01", "2026-10-01")],
      orgUnitName: null,
    });

    expect(screen.getAllByTestId("overlap-bar-e1")).toHaveLength(8);
  });

  it("collapses beyond 5 rows behind a 'View all' expander", async () => {
    const user = userEvent.setup();
    const employees = Array.from({ length: 7 }, (_, i) => employee(`e${i}`, `Employee ${i}`, "2026-09-17", "2026-09-17"));
    renderCalendar({ count: 7, employees, orgUnitName: null });

    expect(screen.queryByText("Employee 5")).toBeNull();
    expect(screen.queryByText("Employee 6")).toBeNull();
    const expandButton = screen.getByRole("button", { name: "Tümünü gör (7)" });

    await user.click(expandButton);

    expect(screen.getByText("Employee 5")).toBeTruthy();
    expect(screen.getByText("Employee 6")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Tümünü gör/ })).toBeNull();
  });

  it("shows a 'not listed' note when the server-side cap hid some overlaps", () => {
    renderCalendar({
      count: 25,
      employees: [employee("e1", "Ada Lovelace", "2026-09-17", "2026-09-17")],
      orgUnitName: null,
    });

    expect(screen.getByText("+24 kişi daha (listelenmedi)")).toBeTruthy();
  });

  it("does not render the expander when there are 5 or fewer employees", () => {
    const employees = Array.from({ length: 5 }, (_, i) => employee(`e${i}`, `Employee ${i}`, "2026-09-17", "2026-09-17"));
    renderCalendar({ count: 5, employees, orgUnitName: null });

    expect(screen.queryByRole("button", { name: /Tümünü gör/ })).toBeNull();
  });
});
