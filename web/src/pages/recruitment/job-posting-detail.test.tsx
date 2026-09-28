import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trRecruitment from "../../../public/locales/tr/recruitment.json";
import trCommon from "../../../public/locales/tr/common.json";
import JobPostingDetailPage from "./job-posting-detail";
import type { JobPosting } from "@/services/recruitment.service";
import type { UserListItem } from "@/services/users.service";

// TK-023: ilana kimlerin erişebileceğini gösteren/yöneten "Erişim" bölümünü doğrular.
const permissions = vi.hoisted(() => ({ granted: new Set<string>(["recruitment.postings.read"]) }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

const posting: JobPosting = {
  id: "posting-1",
  title: "Kıdemli Yazılım Mühendisi",
  orgUnitId: "org-1",
  locationId: null,
  employmentType: "fullTime",
  description: null,
  companyNotes: null,
  ownerUserId: "user-owner",
  hiringManagerUserId: null,
  requisitionId: null,
  status: "open",
  applySlug: "kidemli-yazilim-muhendisi",
  closingDate: null,
  publishedAt: "2026-09-01T00:00:00Z",
  archivedAt: null,
  archiveReason: null,
  accessUserIds: [],
};

const users: UserListItem[] = [
  { id: "user-1", email: "ayse@acme.test", firstName: "Ayşe", lastName: "Yılmaz", phone: null, status: "active", employeeId: null, mfaEnabled: true, lastLoginAt: null, roles: [], createdAt: "2026-01-01T00:00:00Z" },
  { id: "user-2", email: "mehmet@acme.test", firstName: "Mehmet", lastName: "Demir", phone: null, status: "active", employeeId: null, mfaEnabled: true, lastLoginAt: null, roles: [], createdAt: "2026-01-01T00:00:00Z" },
];

const getJobPostingAccess = vi.fn(async (_id: string) => ["user-1"]);
const setJobPostingAccess = vi.fn(async (_id: string, _userIds: string[]) => undefined);

vi.mock("@/services/recruitment.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/recruitment.service")>("@/services/recruitment.service");
  return {
    ...actual,
    getJobPosting: vi.fn(async () => posting),
    getJobPostingAccess: (id: string) => getJobPostingAccess(id),
    setJobPostingAccess: (id: string, userIds: string[]) => setJobPostingAccess(id, userIds),
    listApplicationsByPosting: vi.fn(async () => []),
    listCandidates: vi.fn(async () => []),
    listFormTemplates: vi.fn(async () => []),
  };
});

vi.mock("@/services/users.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/users.service")>("@/services/users.service");
  return {
    ...actual,
    listUsers: vi.fn(async () => ({ items: users, page: 1, pageSize: 200, totalCount: users.length, totalPages: 1, hasNext: false, hasPrevious: false })),
  };
});

vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "recruitment",
    ns: ["recruitment", "common"],
    resources: { tr: { recruitment: trRecruitment, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={["/app/recruitment/postings/posting-1"]}>
            <Routes>
              <Route path="/app/recruitment/postings/:id" element={<JobPostingDetailPage />} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("JobPostingDetail access section (TK-023)", () => {
  it("shows the current access list by user name", async () => {
    permissions.granted = new Set(["recruitment.postings.read"]);
    renderPage();

    expect(await screen.findByText("Ayşe Yılmaz")).toBeTruthy();
    expect(screen.queryByText("Mehmet Demir")).toBeNull();
  });

  it("hides editing controls without recruitment.postings.update", async () => {
    permissions.granted = new Set(["recruitment.postings.read"]);
    renderPage();

    await screen.findByText("Ayşe Yılmaz");
    expect(screen.queryByRole("combobox", { name: trRecruitment.postings.access })).toBeNull();
    expect(screen.queryByRole("button", { name: trCommon.save })).toBeNull();
  });

  it("shows an editable access field and saves it with the posting id and current user ids", async () => {
    permissions.granted = new Set(["recruitment.postings.read", "recruitment.postings.update"]);
    const user = userEvent.setup();
    renderPage();

    // Mantine's MultiSelect popover needs real floating-ui layout to open its option list, which jsdom
    // cannot provide; this asserts the field is present and editable, then exercises the actual wiring
    // (save sends the posting id + the currently selected user ids) via the pill's remove control.
    await screen.findByRole("combobox", { name: trRecruitment.postings.access });
    const removeButtons = document.querySelectorAll(".mantine-Pill-remove");
    expect(removeButtons.length).toBe(1);
    await user.click(removeButtons[0] as HTMLElement);

    await user.click(screen.getByRole("button", { name: trCommon.save }));

    await waitFor(() => expect(setJobPostingAccess).toHaveBeenCalledTimes(1));
    expect(setJobPostingAccess).toHaveBeenCalledWith("posting-1", []);
  });
});
