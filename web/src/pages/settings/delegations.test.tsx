import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trDelegations from "../../../public/locales/tr/delegations.json";
import trCommon from "../../../public/locales/tr/common.json";
import DelegationsPage from "./delegations";
import type { Delegation } from "@/services/workflow-delegations.service";

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true, useAnyPermission: () => true };
});
vi.mock("@/store/auth.store", () => ({
  useAuthStore: (selector: (s: unknown) => unknown) => selector({ user: { id: "me-user", employeeId: "me-emp" } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/ui/date-picker", () => ({
  DatePicker: ({ id, value, onChange }: { id?: string; value: string; onChange: (v: string) => void }) => (
    <input data-testid={id} value={value} onChange={(e) => onChange(e.target.value)} />
  ),
}));
vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ onChange }: { onChange: (v: { id: string; displayName: string } | null) => void }) => (
    <>
      <button onClick={() => onChange({ id: "me-emp", displayName: "Ben" })}>pick-self</button>
      <button onClick={() => onChange({ id: "e2", displayName: "Ali" })}>pick-other</button>
    </>
  ),
}));
vi.mock("@/services/employees.service", () => ({
  getEmployee: vi.fn(async (id: string) => ({ id, userId: id === "me-emp" ? "me-user" : "u2" })),
}));

const day = (offset: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const ITEMS: Delegation[] = [
  { id: "d1", delegatorUserId: "me-user", delegateUserId: "u2", from: day(-1), to: day(3), isActive: true, delegateName: "Ali" },
  { id: "d2", delegatorUserId: "me-user", delegateUserId: "u3", from: day(5), to: day(9), isActive: true, delegateName: "Veli" },
];

const svc = vi.hoisted(() => ({ list: vi.fn(), create: vi.fn(), revoke: vi.fn() }));
vi.mock("@/services/workflow-delegations.service", () => ({
  listDelegations: svc.list,
  createDelegation: svc.create,
  revokeDelegation: svc.revoke,
}));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (q: string) => ({ matches: false, media: q, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => false }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});
beforeEach(() => {
  svc.list.mockReset().mockResolvedValue(ITEMS);
  svc.create.mockReset().mockResolvedValue("new");
  svc.revoke.mockReset().mockResolvedValue(undefined);
});

function renderPage() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr", fallbackLng: "tr", defaultNS: "delegations", ns: ["delegations", "common"],
    resources: { tr: { delegations: trDelegations, common: trCommon } },
    interpolation: { escapeValue: false }, react: { useSuspense: false },
  });
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={qc}>
        <MantineProvider>
          <DelegationsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("DelegationsPage", () => {
  it("end_before_start_invalid: blocks saving when the end date precedes the start", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getByText("pick-other"));
    fireEvent.change(screen.getByTestId("delegation-from"), { target: { value: day(5) } });
    fireEvent.change(screen.getByTestId("delegation-to"), { target: { value: day(2) } });
    fireEvent.click(screen.getByRole("button", { name: trDelegations.form.submit }));
    expect(await screen.findByText(trDelegations.errors.endBeforeStart)).toBeTruthy();
    expect(svc.create).not.toHaveBeenCalled();
  });

  it("cannot_pick_self: rejects delegating to oneself", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getByText("pick-self"));
    fireEvent.change(screen.getByTestId("delegation-from"), { target: { value: day(1) } });
    fireEvent.change(screen.getByTestId("delegation-to"), { target: { value: day(2) } });
    fireEvent.click(screen.getByRole("button", { name: trDelegations.form.submit }));
    expect(await screen.findByText(trDelegations.errors.self)).toBeTruthy();
    expect(svc.create).not.toHaveBeenCalled();
  });

  it("creates a delegation with the delegate's user id", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getByText("pick-other"));
    fireEvent.change(screen.getByTestId("delegation-from"), { target: { value: day(1) } });
    fireEvent.change(screen.getByTestId("delegation-to"), { target: { value: day(2) } });
    fireEvent.click(screen.getByRole("button", { name: trDelegations.form.submit }));
    await waitFor(() => expect(svc.create).toHaveBeenCalledWith({ delegateUserId: "u2", from: day(1), to: day(2) }));
  });

  it("revoke_calls_service: the revoke button calls revokeDelegation", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getAllByRole("button", { name: trDelegations.list.revoke })[0]);
    await waitFor(() => expect(svc.revoke).toHaveBeenCalledWith("d1"));
  });

  it("active_badge_by_date: only a delegation covering today is badged active", async () => {
    renderPage();
    await screen.findByText("Ali");
    expect(screen.getAllByText(trDelegations.status.active)).toHaveLength(1);
    expect(screen.getAllByText(trDelegations.status.scheduled)).toHaveLength(1);
  });
});
