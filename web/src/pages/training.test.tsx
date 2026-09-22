import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trTraining from "../../public/locales/tr/training.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import TrainingPage from "./training";
import {
  activateTrainingProgram,
  completeEnrollment,
  type Enrollment,
  type MyTraining,
  type TrainingProgram,
} from "@/services/training.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return {
    ...actual,
    usePermission: (key: string) => permissions.granted.has(key),
    useAnyPermission: (keys: string[]) => keys.some((key) => permissions.granted.has(key)),
  };
});

const PROGRAMS: TrainingProgram[] = [
  {
    id: "p1",
    title: "İSG temel eğitimi",
    category: "Compliance",
    startDate: "2026-10-01T06:00:00Z",
    endDate: "2026-10-01T15:00:00Z",
    maxCapacity: 2,
    status: "Draft",
  },
  {
    id: "p2",
    title: "React ileri seviye",
    category: "Technical",
    startDate: "2026-09-01T06:00:00Z",
    endDate: "2026-09-03T15:00:00Z",
    status: "Active",
  },
];

const ENROLLMENTS: Enrollment[] = [
  {
    id: "en1",
    trainingProgramId: "p1",
    employeeId: "e1",
    status: "Enrolled",
    enrolledAt: "2026-09-20T08:00:00Z",
  },
];

const MINE: MyTraining[] = [
  {
    enrollmentId: "en9",
    trainingProgramId: "p2",
    title: "React ileri seviye",
    category: "Technical",
    startDate: "2026-09-01T06:00:00Z",
    programStatus: "Active",
    status: "Completed",
    enrolledAt: "2026-08-20T08:00:00Z",
    certificateNumber: "CERT-42",
  },
];

vi.mock("@/services/training.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/training.service")>(
    "@/services/training.service"
  );
  return {
    ...actual,
    listTrainingPrograms: vi.fn(async () => PROGRAMS),
    getMyTrainings: vi.fn(async () => MINE),
    listEnrollments: vi.fn(async () => ENROLLMENTS),
    listTrainingRequirements: vi.fn(async () => []),
    activateTrainingProgram: vi.fn(async () => {}),
    completeEnrollment: vi.fn(async () => {}),
  };
});

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>(
    "@/services/employees.service"
  );
  return { ...actual, getEmployee: vi.fn(async () => ({ id: "e1", displayName: "Ayşe Demir" })) };
});

// jsdom has no matchMedia/ResizeObserver; Mantine's Card/Table/Modal need these to render.
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

beforeEach(() => {
  vi.clearAllMocks();
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "training",
    ns: ["training", "common", "employees"],
    resources: { tr: { training: trTraining, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <TrainingPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("TrainingPage", () => {
  it("shows an employee their own trainings with status and certificate", async () => {
    permissions.granted = new Set(["training.my.enrollments"]);
    renderPage();

    expect(await screen.findByText("React ileri seviye")).toBeInTheDocument();
    expect(screen.getByText("Tamamladı")).toBeInTheDocument();
    expect(screen.getByText(/CERT-42/)).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Programlar" })).not.toBeInTheDocument();
  });

  it("lists programs newest first and lets HR activate a draft", async () => {
    permissions.granted = new Set(["training.programs.view", "training.programs.manage"]);
    const user = userEvent.setup();
    renderPage();

    await screen.findByText("İSG temel eğitimi");
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]!).getByText("İSG temel eğitimi")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("Taslak")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("Mevzuat / uyum")).toBeInTheDocument();
    expect(within(rows[2]!).queryByRole("button", { name: "Etkinleştir" })).not.toBeInTheDocument();

    await user.click(within(rows[1]!).getByRole("button", { name: "Etkinleştir" }));
    await waitFor(() => expect(activateTrainingProgram).toHaveBeenCalledWith("p1"));
  });

  it("opens a program's participants and records completion with a certificate number", async () => {
    permissions.granted = new Set([
      "training.programs.view",
      "training.enrollments.view",
      "training.enrollments.manage",
    ]);
    const user = userEvent.setup();
    renderPage();

    const row = (await screen.findByText("İSG temel eğitimi")).closest("tr")!;
    await user.click(within(row).getByRole("button", { name: "Detay" }));

    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByRole("link", { name: "Ayşe Demir" })).toBeInTheDocument();
    expect(within(dialog).getByText("1 / 2 kişi")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Tamamlandı" }));
    await user.type(await screen.findByLabelText("Sertifika no"), "ISG-2026-1");
    const completeDialog = screen.getByRole("dialog", {
      name: "Eğitimi tamamlandı olarak işaretle",
    });
    await user.click(within(completeDialog).getByRole("button", { name: "Tamamlandı" }));

    await waitFor(() => expect(completeEnrollment).toHaveBeenCalledWith("en1", "ISG-2026-1"));
  });
});
