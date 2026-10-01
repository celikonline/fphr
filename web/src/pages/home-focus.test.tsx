import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trHome from "../../public/locales/tr/home.json";
import { listInbox, type InboxTaskDto } from "@/services/workflow.service";
import { HomeFocus } from "./home-focus";

vi.mock("@/services/workflow.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/workflow.service")>("@/services/workflow.service");
  return { ...actual, listInbox: vi.fn() };
});

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

const inbox = vi.mocked(listInbox);

function renderFocus(
  props: { canViewInbox: boolean; canRequestLeave: boolean; canViewDirectory: boolean },
  seededInbox?: InboxTaskDto[],
  providedClient?: QueryClient,
) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "home",
    ns: ["home"],
    resources: { tr: { home: trHome } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = providedClient ?? new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (seededInbox) queryClient.setQueryData(["home", "pending-approvals"], seededInbox);
  const view = render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <HomeFocus {...props} />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return { ...view, queryClient };
}

describe("HomeFocus", () => {
  beforeEach(() => inbox.mockReset());

  it("does not request inbox data or show an approval action without permission", async () => {
    renderFocus({ canViewInbox: false, canRequestLeave: true, canViewDirectory: false });
    expect(screen.getByRole("link", { name: "İzinlerime git" })).toHaveAttribute("href", "/app/leave");
    expect(screen.queryByText(/onay bekliyor/i)).not.toBeInTheDocument();
    await waitFor(() => expect(inbox).not.toHaveBeenCalled());
  });

  it("ignores cached approval data after inbox permission is removed", () => {
    renderFocus(
      { canViewInbox: false, canRequestLeave: false, canViewDirectory: true },
      [{ taskId: "t1", approvalRequestId: "a1", requestType: "leave", subjectId: "s1", stepNumber: 1, isFallbackRoleTask: false }],
    );
    expect(screen.getByRole("link", { name: "Çalışanları gör" })).toHaveAttribute("href", "/app/employees");
    expect(screen.queryByRole("link", { name: "Onayları aç" })).not.toBeInTheDocument();
    expect(inbox).not.toHaveBeenCalled();
  });

  it("surfaces the real pending approval count and inbox action", async () => {
    inbox.mockResolvedValueOnce([
      { taskId: "t1", approvalRequestId: "a1", requestType: "leave", subjectId: "s1", stepNumber: 1, isFallbackRoleTask: false },
      { taskId: "t2", approvalRequestId: "a2", requestType: "leave", subjectId: "s2", stepNumber: 1, isFallbackRoleTask: false },
    ]);
    renderFocus({ canViewInbox: true, canRequestLeave: true, canViewDirectory: true });
    expect(await screen.findByText("2 talep onayınızı bekliyor")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Onayları aç" })).toHaveAttribute("href", "/app/inbox");
  });

  it("uses a permitted fallback without inventing work when inbox is empty or fails", async () => {
    inbox.mockResolvedValueOnce([]);
    const view = renderFocus({ canViewInbox: true, canRequestLeave: false, canViewDirectory: true });
    expect(await screen.findByRole("link", { name: "Çalışanları gör" })).toHaveAttribute("href", "/app/employees");
    expect(screen.queryByText(/onayınızı bekliyor/i)).not.toBeInTheDocument();
    view.unmount();

    // Seed a settled error with its fetch rejection handled here, then render without a refetch.
    // This verifies the component's error-state fallback without leaving an async retry behind the test.
    const errorClient = new QueryClient({ defaultOptions: { queries: { retry: false, retryOnMount: false } } });
    await errorClient.fetchQuery({
      queryKey: ["home", "pending-approvals"],
      queryFn: async () => { throw new Error("offline"); },
      retry: false,
    }).catch(() => undefined);
    const failedView = renderFocus({ canViewInbox: true, canRequestLeave: false, canViewDirectory: false }, undefined, errorClient);
    expect(screen.getByRole("link", { name: "Hesabıma git" })).toHaveAttribute("href", "/app/account");
    expect(failedView.queryClient.getQueryState(["home", "pending-approvals"])?.status).toBe("error");
    expect(inbox).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/onayınızı bekliyor/i)).not.toBeInTheDocument();
  });
});
