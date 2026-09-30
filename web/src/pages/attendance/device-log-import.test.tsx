import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import DeviceLogImportPage from "./device-log-import";
import { importDeviceLogs } from "@/services/attendance.service";
import { toast } from "@/hooks/use-toast";

vi.mock("@/services/attendance.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance.service")>(
    "@/services/attendance.service"
  );
  return { ...actual, importDeviceLogs: vi.fn() };
});
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/permission-guard", () => ({ usePermission: () => true }));

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
});

afterEach(() => vi.clearAllMocks());

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "attendance",
    ns: ["attendance", "common"],
    resources: { tr: { attendance: trAttendance, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <DeviceLogImportPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("DeviceLogImportPage", () => {
  it("shows the translated period-locked message instead of the raw server detail", async () => {
    vi.mocked(importDeviceLogs).mockRejectedValue(
      new AxiosError("locked", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 409,
        data: { code: "attendance.timesheet.period_locked", detail: "raw server detail" },
      } as never)
    );
    const user = userEvent.setup();
    const { container } = renderPage();

    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(["employeeCode;timestamp"], "log.csv", { type: "text/csv" }));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive", description: trAttendance.errors.periodLocked })
      )
    );
  });
});
