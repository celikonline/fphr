import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trShifts from "../../../public/locales/tr/shifts.json";
import trCommon from "../../../public/locales/tr/common.json";
import ShiftDefinitionsPage from "./shift-definitions";
import { useAuthStore } from "@/store/auth.store";
import { emptyShiftDefinition } from "@/components/attendance/shift-definition-form";
import { listShiftDefinitions, saveShiftDefinition } from "@/services/attendance-shift-definitions.service";

vi.mock("@/services/attendance-shift-definitions.service", () => ({
  listShiftDefinitions: vi.fn(),
  saveShiftDefinition: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (q: string) => ({ matches: false, media: q, addEventListener() {}, removeEventListener() {} }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

const existing = { ...emptyShiftDefinition(), id: "s1", code: "GUN", name: "Gündüz" };

async function renderPage(granted: string[]) {
  useAuthStore.setState({
    hasPermission: (p: string) => granted.includes(p),
    isAccessSettingEnabled: () => true,
  });
  const i18n = i18next.createInstance();
  await i18n.init({
    lng: "tr",
    resources: { tr: { shifts: trShifts, common: trCommon } },
    defaultNS: "shifts",
  });
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MantineProvider>
          <ShiftDefinitionsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("ShiftDefinitionsPage", () => {
  beforeEach(() => {
    vi.mocked(listShiftDefinitions).mockResolvedValue([existing]);
    vi.mocked(saveShiftDefinition).mockResolvedValue(existing);
  });

  it("save_and_apply_past_calls_service_with_flag", async () => {
    const user = userEvent.setup();
    await renderPage(["attendance.shiftdefinition.view", "attendance.shiftdefinition.manage"]);
    await user.click(await screen.findByText("Gündüz"));
    await user.click(await screen.findByRole("button", { name: "Kaydet ve geçmişe uygula" }));
    await waitFor(() =>
      expect(saveShiftDefinition).toHaveBeenCalledWith(expect.objectContaining({ id: "s1" }), { applyToPast: true }),
    );
  });

  it("read-only users cannot create or edit", async () => {
    await renderPage(["attendance.shiftdefinition.view"]);
    expect(await screen.findByText("Gündüz")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Yeni vardiya" })).toBeNull();
  });
});
