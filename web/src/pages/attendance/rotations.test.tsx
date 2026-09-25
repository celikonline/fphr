import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trRotations from "../../../public/locales/tr/rotations.json";
import trCommon from "../../../public/locales/tr/common.json";
import RotationsPage from "./rotations";
import { useAuthStore } from "@/store/auth.store";
import { assignRotation, listRotations, type Rotation } from "@/services/attendance-rotations.service";
import { listShiftDefinitions } from "@/services/attendance-shift-definitions.service";

vi.mock("@/services/attendance-rotations.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance-rotations.service")>(
    "@/services/attendance-rotations.service"
  );
  return { ...actual, listRotations: vi.fn(), saveRotation: vi.fn(), assignRotation: vi.fn() };
});
vi.mock("@/services/attendance-shift-definitions.service", () => ({ listShiftDefinitions: vi.fn() }));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ onChange }: { onChange: (v: { id: string; displayName: string }) => void }) => (
    <button type="button" onClick={() => onChange({ id: "emp-1", displayName: "Ayşe Yılmaz" })}>
      pick-employee
    </button>
  ),
}));

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

const PATTERN: Rotation = {
  id: "rot-1",
  code: "R1",
  name: "Gece döngüsü",
  preset: "threeByEight",
  cycleLengthDays: 3,
  steps: [],
  isActive: true,
};

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: () => true,
    isAccessSettingEnabled: () => true,
  });
  vi.mocked(listRotations).mockResolvedValue([PATTERN]);
  vi.mocked(listShiftDefinitions).mockResolvedValue([]);
});
afterEach(() => vi.clearAllMocks());

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "rotations",
    ns: ["rotations", "common"],
    resources: { tr: { rotations: trRotations, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <RotationsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

async function openAssignAndFill(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("tab", { name: "Atama" }));
  await user.click((await screen.findAllByLabelText("Kalıp"))[0]);
  await user.click(await screen.findByRole("option", { name: /Gece döngüsü/ }));
  await user.click(screen.getByRole("button", { name: "pick-employee" }));
  await user.type(screen.getByLabelText(/^Başlangıç/), "2026-10-01");
}

describe("RotationsPage", () => {
  it("preset_prefills_steps", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Yeni kalıp" }));
    await user.click(screen.getByText("2×12"));
    const steps = await screen.findAllByTestId("rotation-step");
    expect(steps).toHaveLength(4);
    expect(screen.getByLabelText("Döngü uzunluğu (gün)")).toHaveValue("4");

    await user.click(screen.getByRole("radio", { name: "3×8" }));
    await waitFor(() => expect(screen.getAllByTestId("rotation-step")).toHaveLength(3));
  });

  it("apply_shows_skipped_locked_months", async () => {
    vi.mocked(assignRotation).mockResolvedValue({ createdCount: 12, skippedLockedMonths: ["2026-08"] });
    const user = userEvent.setup();
    renderPage();
    await openAssignAndFill(user);
    await user.click(screen.getByRole("button", { name: "Ata" }));
    const skipped = await screen.findByTestId("rotation-skipped");
    expect(within(skipped).getByText(/2026-08/)).toBeInTheDocument();
    expect(screen.getByText("12 taslak vardiya ataması oluşturuldu.")).toBeInTheDocument();
    expect(assignRotation).toHaveBeenCalledWith(
      "rot-1",
      expect.objectContaining({ employeeIds: ["emp-1"], startDate: "2026-10-01", offsetDays: 0 })
    );
  });

  it("overlap_error_displayed", async () => {
    vi.mocked(assignRotation).mockRejectedValue(
      new AxiosError("conflict", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 409,
        data: { detail: "Ayşe Yılmaz için çakışan rotasyon ataması var." },
      } as never)
    );
    const user = userEvent.setup();
    renderPage();
    await openAssignAndFill(user);
    await user.click(screen.getByRole("button", { name: "Ata" }));
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Ayşe Yılmaz için çakışan rotasyon ataması var.");
  });
});
