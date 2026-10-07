import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveTypeDialog } from "./leave-type-dialog";
import { updateLeaveType, type LeaveRuleSet, type LeaveType } from "@/services/leave.service";

// senseik-008 Bug 1 regression: the seed name is a raw resource key ("leave.type.annual") until the user edits it.
// LeaveTypeDialog must resolve it through useLeaveName() for display and on save — never show/send the raw key.
vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>("@/services/leave.service");
  return { ...actual, addLeaveType: vi.fn(), updateLeaveType: vi.fn() };
});

// jsdom has no matchMedia/ResizeObserver/document.fonts; LeaveTypeDialog's useMediaQuery, Mantine's Select/Combobox,
// and the autosize Textarea (description field) each need one of these mounted to render without throwing.
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
    defaultNS: "leave",
    ns: ["leave", "common"],
    resources: { tr: { leave: trLeave, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderDialog(editing: LeaveType | null) {
  const ruleSet: LeaveRuleSet = {
    id: "rule-set-1",
    name: "Varsayılan",
    isDefault: true,
    isSystemSeed: false,
    unpaidLeaveShiftsAccrual: false,
    assignedEmployeeCount: 0,
    types: editing ? [editing] : [],
  };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const onClose = vi.fn();

  const view = render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveTypeDialog ruleSet={ruleSet} editing={editing} onClose={onClose} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );

  return { onClose, ...view };
}

function seededAnnualType(): LeaveType {
  return {
    id: "type-1",
    sortOrder: 0,
    settings: {
      code: "annual",
      // Seeded system rule sets store the resource key, not a display string (see leave-shared.tsx useLeaveName).
      name: "leave.type.annual",
      description: null,
      isPaid: true,
      limitType: "perYear",
      perRequestMaxDays: null,
      annualDays: 14,
      ageExceptionEnabled: false,
      ageExceptionMinDays: null,
      negativeBalance: "none",
      negativeBalanceLimitDays: null,
      accrualFrequency: "yearly",
      accrualTiming: "endOfPeriod",
      accrualBasis: "hireDate",
      carryOverMode: "reset",
      carryOverMaxDays: null,
      seniorityTiers: [],
      minConsecutiveDays: null,
      maxConsecutiveDays: null,
      unit: "fullDay",
      requiresDescription: false,
      requiresSubstitute: false,
      requiresDocument: false,
      waitingPeriodMonths: 0,
      countsNonWorkingDays: false,
      countsTowardAccrualService: true,
      showTypeNameOnCalendar: true,
      isSensitive: false,
      allowSelfServiceRequest: true,
      isDisabled: false,
      isStatutory: false,
    },
  };
}

describe("LeaveTypeDialog (senseik-021 form modal alignment)", () => {
  it("opens left-aligned via the shared FormModal wrapper (inner justify-content: flex-start)", () => {
    renderDialog(seededAnnualType());

    // Mantine's ModalBaseContent renders the "inner" flex wrapper as the dialog's direct parent
    // (FocusTrap clones its child rather than adding a DOM node of its own).
    const inner = screen.getByRole("dialog").parentElement;
    expect(inner).not.toBeNull();
    expect(inner?.style.justifyContent).toBe("flex-start");
  });
});

describe("LeaveTypeDialog (senseik-038 fieldset/grid width regression)", () => {
  it("keeps every direct CSS Grid inside a Fieldset at full width (w-full)", () => {
    // fieldset varsayılan olarak min-content genişlikte boyutlanır; bu, doğrudan altındaki CSS Grid'in
    // minmax(0,1fr) kolonlarıyla çakışıp grid'i 0px genişliğe çökertiyordu (Ayarlar > İzin kuralları >
    // Düzenle tek karakter genişliğinde bir şeride dönüşüyordu). Erişilebilirlik sorgularıyla ifade
    // edilemeyen saf bir CSS regresyon koruması olduğundan burada querySelector kullanılıyor.
    // Mantine `Modal` içeriğini `document.body`'e portallıyor — RTL'nin `render()`'dan dönen `container`'ı
    // yalnız ilk mount div'ini kapsıyor, portallanan içeriği görmüyor; bu yüzden `document.body` üzerinden ara.
    renderDialog(seededAnnualType());

    const grids = document.body.querySelectorAll<HTMLDivElement>(".grid");
    expect(grids.length).toBeGreaterThanOrEqual(6);
    grids.forEach((grid) => {
      expect(grid.classList.contains("w-full")).toBe(true);
    });
  });
});

describe("LeaveTypeDialog (senseik-008 Bug 1)", () => {
  it("shows the resolved leave name, not the raw seed key, in the name field", () => {
    renderDialog(seededAnnualType());

    const nameInput = screen.getByRole("textbox", { name: /Tür adı/ }) as HTMLInputElement;
    expect(nameInput.value).toBe("Yıllık izin");
    expect(nameInput.value).not.toContain("leave.type.annual");
    expect(screen.queryByText("leave.type.annual")).toBeNull();
  });

  it("saves the resolved display name (not the raw key) when the user does not touch the name field", async () => {
    const user = userEvent.setup();
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(updateLeaveType).toHaveBeenCalledTimes(1);
    const [, , payload] = vi.mocked(updateLeaveType).mock.calls[0];
    expect(payload.name).toBe("Yıllık izin");
    expect(payload.name).not.toBe("leave.type.annual");
  });

  it("saves the user's edited value verbatim (raw override), not a re-resolved key", async () => {
    const user = userEvent.setup();
    renderDialog(seededAnnualType());

    const nameInput = screen.getByRole("textbox", { name: /Tür adı/ });
    await user.clear(nameInput);
    await user.type(nameInput, "Özel yıllık izin türü");
    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(updateLeaveType).toHaveBeenCalledTimes(1);
    const [, , payload] = vi.mocked(updateLeaveType).mock.calls[0];
    expect(payload.name).toBe("Özel yıllık izin türü");
  });
});

describe("LeaveTypeDialog (denetim A6 eligible gender)", () => {
  function seededPaternityType(): LeaveType {
    const annual = seededAnnualType();
    return {
      ...annual,
      id: "type-2",
      settings: { ...annual.settings, code: "paternity", name: "leave.type.paternity", limitType: "perRequest", perRequestMaxDays: 10, annualDays: null, eligibleGender: "male" },
    };
  }

  it("shows the stored eligible gender and sends it back on save", async () => {
    const user = userEvent.setup();
    renderDialog(seededPaternityType());

    const select = screen.getByRole("combobox", { name: /Uygun cinsiyet/ }) as HTMLInputElement;
    expect(select.value).toBe("Yalnızca erkek");

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(updateLeaveType).toHaveBeenCalled();
    const calls = vi.mocked(updateLeaveType).mock.calls;
    const [, , payload] = calls[calls.length - 1];
    expect(payload.eligibleGender).toBe("male");
  });

  it("defaults a new type to everyone", () => {
    renderDialog(null);

    expect((screen.getByRole("combobox", { name: /Uygun cinsiyet/ }) as HTMLInputElement).value).toBe("Herkes");
  });
});

describe("LeaveTypeDialog (configurable birthday and hourly policies)", () => {
  it("renders and saves birthday-only and leap-day settings", async () => {
    const user = userEvent.setup();
    const annual = seededAnnualType();
    renderDialog({
      ...annual,
      settings: { ...annual.settings, code: "birthday", name: "Doğum günü izni", useOnlyOnBirthday: true, feb29Policy: "useMarch1" },
    });

    expect(screen.getByRole("checkbox", { name: /Yalnızca doğum gününde kullanılabilir/ })).toBeChecked();
    expect((screen.getByRole("combobox", { name: /29 Şubat doğum günü/ }) as HTMLInputElement).value).toBe("1 Mart");
    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    const calls = vi.mocked(updateLeaveType).mock.calls;
    const [, , payload] = calls[calls.length - 1];
    expect(payload.useOnlyOnBirthday).toBe(true);
    expect(payload.feb29Policy).toBe("useMarch1");
  });

  it("renders the optional hourly threshold and day fraction", () => {
    const annual = seededAnnualType();
    renderDialog({
      ...annual,
      settings: { ...annual.settings, code: "jobSearch", unit: "hourly", hourlyHalfDayThresholdMinutes: 180, hourlyHalfDayDays: 0.5 },
    });

    expect(screen.getByLabelText(/Yarım güne dönüşüm eşiği/)).toHaveValue("180");
    expect(screen.getByLabelText(/Eşik aşılınca düşülecek gün/)).toHaveValue("0.5");
  });
});

describe("LeaveTypeDialog (TK-012 field-scoped server errors)", () => {
  function axiosError(data: unknown) {
    return { isAxiosError: true, response: { status: 400, data } };
  }

  it("binds a server field error to its input, focuses it, and skips the generic banner", async () => {
    const user = userEvent.setup();
    vi.mocked(updateLeaveType).mockRejectedValueOnce(
      axiosError({ detail: "Yıllık gün 0.5 ile 365 arasında olmalıdır.", code: "leave.type.invalid", args: { field: "annualDays" } }),
    );
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    const annualDaysInput = await screen.findByLabelText(/Yıllık gün/);
    expect(annualDaysInput).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Yıllık gün 0.5 ile 365 arasında olmalıdır.")).toBeInTheDocument();
    expect(annualDaysInput).toHaveFocus();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("falls back to the generic banner when the field is not rendered (e.g. seniorityTiers)", async () => {
    const user = userEvent.setup();
    vi.mocked(updateLeaveType).mockRejectedValueOnce(
      axiosError({ detail: "Kıdem kademeleri geçersiz.", code: "leave.type.invalid", args: { field: "seniorityTiers" } }),
    );
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Kıdem kademeleri geçersiz.");
  });

  it("falls back to the generic banner when the server sends no field", async () => {
    const user = userEvent.setup();
    vi.mocked(updateLeaveType).mockRejectedValueOnce(axiosError({ detail: "Sunucu hatası." }));
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Sunucu hatası.");
  });

  it("clears the field error once the user edits the field", async () => {
    const user = userEvent.setup();
    vi.mocked(updateLeaveType).mockRejectedValueOnce(
      axiosError({ detail: "Yıllık gün geçersiz.", code: "leave.type.invalid", args: { field: "annualDays" } }),
    );
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));
    const annualDaysInput = await screen.findByLabelText(/Yıllık gün/);
    expect(annualDaysInput).toHaveAttribute("aria-invalid", "true");

    await user.clear(annualDaysInput);
    await user.type(annualDaysInput, "10");

    expect(annualDaysInput).not.toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByText("Yıllık gün geçersiz.")).toBeNull();
  });

  it("shows the client-side name-required error under the name field, not the generic banner", async () => {
    const user = userEvent.setup();
    renderDialog(seededAnnualType());

    const nameInput = screen.getByRole("textbox", { name: /Tür adı/ });
    await user.clear(nameInput);
    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(nameInput).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Ad zorunludur.")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(updateLeaveType).not.toHaveBeenCalled();
  });
});
