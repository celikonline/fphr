import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import trCalendar from "../../../public/locales/tr/companyCalendar.json";
import type { CompanyCalendarEntry } from "@/services/company-calendar.service";
import { buildEventPayload, emptyEventForm, validateEventForm } from "./event-form-model";
import CalendarEventChip from "./calendar-event-chip";
import EventFormPanel from "./event-form-panel";

vi.mock("@/services/company-calendar.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/company-calendar.service")>("@/services/company-calendar.service");
  return {
    ...actual,
    createCompanyEvent: vi.fn().mockResolvedValue("new-id"),
    updateCompanyEvent: vi.fn().mockResolvedValue(undefined),
    listCompanyEventCategories: vi.fn().mockResolvedValue([{ id: "cat1", name: "Eğitim", color: "#12b886" }]),
  };
});
vi.mock("@/components/employees/org-unit-select", () => ({ default: () => null }));
vi.mock("@/components/employees/employee-multi-picker", () => ({
  default: ({ onConfirm }: { onConfirm: (s: unknown) => void }) => (
    <button type="button" onClick={() => onConfirm({ employeeIds: ["e1", "e2"], filter: null, excludedEmployeeIds: null })}>
      picker-confirm
    </button>
  ),
}));
import { createCompanyEvent, updateCompanyEvent } from "@/services/company-calendar.service";

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
  Object.defineProperty(document, "fonts", { writable: true, value: { addEventListener: () => {}, removeEventListener: () => {} } });
});

function wrap(ui: React.ReactNode) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "common",
    ns: ["common", "employees", "companyCalendar"],
    resources: { tr: { common: trCommon, employees: trEmployees, companyCalendar: trCalendar } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>{ui}</MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

const baseEntry: CompanyCalendarEntry = {
  id: "ev1",
  title: "Kickoff",
  description: "Açıklama",
  date: "2026-10-05",
  type: "meeting",
  visibilityOrgUnitId: null,
  startsAt: "2026-10-05T07:00:00.000Z",
  endsAt: "2026-10-05T08:00:00.000Z",
  isAllDay: false,
  mode: "online",
  onlineUrl: "https://meet.example.com/x",
  categoryId: "cat1",
  inviteeEmployeeIds: ["e9"],
};

beforeEach(() => {
  vi.mocked(createCompanyEvent).mockClear();
  vi.mocked(updateCompanyEvent).mockClear();
});

describe("event form validation", () => {
  it("online_requires_url", () => {
    const v = { ...emptyEventForm("2026-10-05"), title: "x", mode: "online" as const, onlineUrl: "ftp://bad" };
    expect(validateEventForm(v)).toContain("onlineUrlRequired");
    expect(validateEventForm({ ...v, onlineUrl: "https://a.b/c" })).not.toContain("onlineUrlRequired");
  });

  it("onsite_requires_location", () => {
    const v = { ...emptyEventForm("2026-10-05"), title: "x", mode: "onsite" as const, location: "  " };
    expect(validateEventForm(v)).toContain("locationRequired");
  });

  it("time_range_validation (end before start)", () => {
    const v = { ...emptyEventForm("2026-10-05"), title: "x", isAllDay: false, startTime: "10:00", endTime: "09:00" };
    expect(validateEventForm(v)).toContain("endsBeforeStarts");
    expect(validateEventForm({ ...v, endTime: "10:00" })).not.toContain("endsBeforeStarts");
  });
});

describe("EventFormPanel", () => {
  it("online_requires_url shows the field error and does not submit", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel onClose={() => {}} />);
    await user.type(await screen.findByLabelText(/Başlık/), "Toplantı");
    await user.click(screen.getByRole("radio", { name: "Online" }));
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    expect(await screen.findByText(trCalendar.errors.onlineUrlRequired)).toBeInTheDocument();
    expect(createCompanyEvent).not.toHaveBeenCalled();
  });

  it("onsite_requires_location shows the field error and does not submit", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel onClose={() => {}} />);
    await user.type(await screen.findByLabelText(/Başlık/), "Toplantı");
    await user.click(screen.getByRole("radio", { name: "Yüz yüze" }));
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    expect(await screen.findByText(trCalendar.errors.locationRequired)).toBeInTheDocument();
    expect(createCompanyEvent).not.toHaveBeenCalled();
  });

  it("invitees_from_multi_picker sends the picked ids", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel onClose={() => {}} />);
    await user.type(await screen.findByLabelText(/Başlık/), "Piknik");
    await user.click(screen.getByRole("button", { name: trCalendar.form.selectInvitees }));
    await user.click(await screen.findByText("picker-confirm"));
    await waitFor(() => expect(screen.getByTestId("invitee-count")).toHaveTextContent("2 davetli"));
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    await waitFor(() => expect(createCompanyEvent).toHaveBeenCalled());
    expect(vi.mocked(createCompanyEvent).mock.calls[0][0].inviteeEmployeeIds).toEqual(["e1", "e2"]);
  });

  it("time_range_validation blocks an edit whose end precedes its start", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel entry={{ ...baseEntry, startsAt: baseEntry.endsAt, endsAt: baseEntry.startsAt }} onClose={() => {}} />);
    await user.click(await screen.findByRole("button", { name: trCommon.save }));
    expect(await screen.findByText(trCalendar.errors.endsBeforeStarts)).toBeInTheDocument();
    expect(updateCompanyEvent).not.toHaveBeenCalled();
  });

  it("put_sends_all_fields: editing the title keeps every other field", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel entry={baseEntry} onClose={() => {}} />);
    const title = await screen.findByLabelText(/Başlık/);
    await user.clear(title);
    await user.type(title, "Kickoff 2");
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    await waitFor(() => expect(updateCompanyEvent).toHaveBeenCalled());
    const [id, payload] = vi.mocked(updateCompanyEvent).mock.calls[0];
    expect(id).toBe("ev1");
    expect(payload).toMatchObject({
      title: "Kickoff 2",
      description: "Açıklama",
      eventDate: "2026-10-05",
      type: "meeting",
      visibilityOrgUnitId: null,
      startsAt: baseEntry.startsAt,
      endsAt: baseEntry.endsAt,
      isAllDay: false,
      mode: "online",
      location: null,
      onlineUrl: "https://meet.example.com/x",
      categoryId: "cat1",
      inviteeEmployeeIds: ["e9"],
      notifyInvitees: false,
    });
    expect(Object.keys(payload).sort()).toEqual(Object.keys(buildEventPayload(emptyEventForm())).sort());
  });

  it("all_day_hides_time", async () => {
    const user = userEvent.setup();
    wrap(<EventFormPanel entry={baseEntry} onClose={() => {}} />);
    expect(await screen.findByLabelText(/Başlık/)).toBeInTheDocument();
    expect(document.getElementById("event-start-time")).not.toBeNull();
    await user.click(screen.getByRole("switch", { name: trCalendar.form.allDay }));
    expect(document.getElementById("event-start-time")).toBeNull();
    expect(document.getElementById("event-end-time")).toBeNull();
  });
});

describe("CalendarEventChip", () => {
  it("category_color_in_calendar: colours the chip and names the category", () => {
    wrap(
      <CalendarEventChip
        entry={baseEntry}
        typeLabel="Toplantı"
        category={{ id: "cat1", name: "Eğitim", color: "#12b886" }}
        canManage={false}
        onOpen={() => {}}
        onDelete={() => {}}
      />,
    );
    const chip = screen.getByTestId("calendar-event-chip");
    expect(chip).toHaveAttribute("data-category-color", "#12b886");
    expect(chip.style.borderInlineStart).toContain("3px");
    expect(chip).toHaveTextContent("Eğitim");
    expect(chip).toHaveTextContent(/\d{2}[:.]\d{2}\s?[–-]\s?\d{2}[:.]\d{2}/);
  });
});
