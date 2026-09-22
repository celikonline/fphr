import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trCommon from "../../../public/locales/tr/common.json";
import { DatePicker, parseTypedDate } from "./date-picker";
import type { HolidayCalendarEntry } from "@/services/structure.service";

// senseik-041: date-picker.tsx regression tests (format, outside-click safety, calendar exclusivity, holiday marking).
vi.mock("@/services/structure.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/structure.service")>("@/services/structure.service");
  return { ...actual, listHolidayCalendarEntries: vi.fn() };
});
import { listHolidayCalendarEntries } from "@/services/structure.service";

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Popover/DatePicker calendar needs one of these
// mounted to open without throwing (same mocks as leave-type-dialog.test.tsx).
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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "common",
    ns: ["common"],
    resources: { tr: { common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>{ui}</MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

function holidayEntry(date: string, name: string): HolidayCalendarEntry {
  return { id: date, holidayCalendarId: "cal", date, name, isRecurringAnnually: false };
}

describe("DatePicker (senseik-041)", () => {
  it("displays the value in dd.MM.yyyy format, not mm/dd/yyyy", () => {
    renderWithProviders(<DatePicker value="2026-09-18" onChange={vi.fn()} />);

    expect(screen.getByDisplayValue("18.09.2026")).toBeDefined();
  });

  it("keeps the selected value and never calls onChange when the calendar is dismissed via an outside click", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <div>
        <DatePicker id="start" value="2026-09-18" onChange={onChange} />
        <button type="button">Outside</button>
      </div>,
    );

    await user.click(screen.getByDisplayValue("18.09.2026"));
    await user.click(screen.getByRole("button", { name: "Outside" }));

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByDisplayValue("18.09.2026")).toBeDefined();
  });

  it("closes the first calendar when a second date picker is opened right after (no overlapping popovers)", async () => {
    vi.mocked(listHolidayCalendarEntries).mockImplementation((calendarId: string) =>
      Promise.resolve(calendarId === "cal-a" ? [holidayEntry("2026-09-05", "Holiday A")] : [holidayEntry("2026-09-20", "Holiday B")]),
    );
    const user = userEvent.setup();
    renderWithProviders(
      <div className="grid gap-4 sm:grid-cols-2">
        <DatePicker id="leave-start" value="2026-09-05" onChange={vi.fn()} holidayCalendarId="cal-a" />
        <DatePicker id="leave-end" value="2026-09-20" onChange={vi.fn()} holidayCalendarId="cal-b" />
      </div>,
    );

    await user.click(screen.getByDisplayValue("05.09.2026"));
    expect(await screen.findByTitle("Holiday A")).toBeDefined();

    await user.click(screen.getByDisplayValue("20.09.2026"));
    expect(await screen.findByTitle("Holiday B")).toBeDefined();

    // The first calendar must be fully unmounted, not just visually behind the second one (senseik-041 Bug 2).
    expect(screen.queryByTitle("Holiday A")).toBeNull();
  });

  it("marks a public holiday from the holiday calendar with a distinct, labeled day cell", async () => {
    vi.mocked(listHolidayCalendarEntries).mockResolvedValue([holidayEntry("2026-09-15", "Cumhuriyet Bayramı")]);
    const user = userEvent.setup();
    renderWithProviders(<DatePicker value="2026-09-15" onChange={vi.fn()} holidayCalendarId="cal-tr" />);

    await user.click(screen.getByDisplayValue("15.09.2026"));

    expect(await screen.findByTitle("Cumhuriyet Bayramı")).toBeDefined();
  });

  // N-6: the field is also a text input, so a date can be typed as gg.aa.yyyy.
  it("commits a typed dd.MM.yyyy date as a yyyy-MM-dd value", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(<DatePicker value="2026-09-18" onChange={onChange} />);

    const input = screen.getByDisplayValue("18.09.2026");
    await user.clear(input);
    await user.type(input, "05.10.2026");
    await user.tab();

    expect(onChange).toHaveBeenLastCalledWith("2026-10-05");
  });

  it("ignores an impossible typed date and restores the previous value on blur", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(<DatePicker value="2026-09-18" onChange={onChange} />);

    const input = screen.getByDisplayValue("18.09.2026");
    await user.clear(input);
    await user.type(input, "31.02.2026");
    await user.tab();

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByDisplayValue("18.09.2026")).toBeDefined();
  });

  it("clears the value when the typed text is emptied and the field is clearable", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(<DatePicker value="2026-09-18" onChange={onChange} clearable />);

    await user.clear(screen.getByDisplayValue("18.09.2026"));

    expect(onChange).toHaveBeenLastCalledWith("");
  });
});

describe("parseTypedDate (N-6)", () => {
  it("accepts dd.MM.yyyy, d.M.yyyy and yyyy-MM-dd", () => {
    expect(parseTypedDate("05.10.2026")).toBe("2026-10-05");
    expect(parseTypedDate("5.1.2026")).toBe("2026-01-05");
    expect(parseTypedDate(" 2026-10-05 ")).toBe("2026-10-05");
  });

  it("rejects partial, impossible or ambiguous text", () => {
    expect(parseTypedDate("")).toBeNull();
    expect(parseTypedDate("05.10")).toBeNull();
    expect(parseTypedDate("31.02.2026")).toBeNull();
    expect(parseTypedDate("2026-02-30")).toBeNull();
    expect(parseTypedDate("10/05/2026")).toBeNull();
  });
});
