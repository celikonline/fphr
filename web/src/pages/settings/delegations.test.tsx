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

const perms = vi.hoisted(() => ({ granted: new Set<string>() }));
vi.mock("@/components/permission-guard", () => ({
  usePermission: (p: string) => perms.granted.has(p),
  useAnyPermission: (ps: string[]) => ps.some((p) => perms.granted.has(p)),
}));
vi.mock("@/store/auth.store", () => ({
  useAuthStore: (selector: (s: unknown) => unknown) => selector({ user: { id: "me-user", employeeId: "me-emp", name: "Ben" } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/ui/date-picker", () => ({
  DatePicker: ({ id, value, onChange }: { id?: string; value: string; onChange: (v: string) => void }) => (
    <input data-testid={id} value={value} onChange={(e) => onChange(e.target.value)} />
  ),
}));
vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ label, onChange }: { label?: string; onChange: (v: { id: string; displayName: string } | null) => void }) => (
    <>
      <button onClick={() => onChange({ id: "me-emp", displayName: "Ben" })}>{`pick-self-${label}`}</button>
      <button onClick={() => onChange({ id: "e2", displayName: "Ali" })}>{`pick-other-${label}`}</button>
      <button onClick={() => onChange({ id: "e3", displayName: "Veli" })}>{`pick-third-${label}`}</button>
    </>
  ),
}));

const day = (offset: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const base = { requestTypes: [], isRevoked: false };
const MINE: Delegation[] = [
  { ...base, id: "d1", delegatorEmployeeId: "me-emp", delegateEmployeeId: "e2", startDate: day(-1), endDate: day(3), delegateName: "Ali", delegatorName: "Ben" },
  { ...base, id: "d2", delegatorEmployeeId: "me-emp", delegateEmployeeId: "e3", startDate: day(5), endDate: day(9), delegateName: "Veli", delegatorName: "Ben" },
  { ...base, id: "d3", delegatorEmployeeId: "me-emp", delegateEmployeeId: "e4", startDate: day(-9), endDate: day(-5), delegateName: "Eski", delegatorName: "Ben" },
  { ...base, id: "d4", delegatorEmployeeId: "me-emp", delegateEmployeeId: "e5", startDate: day(-1), endDate: day(3), isRevoked: true, delegateName: "Iptal", delegatorName: "Ben" },
  { ...base, id: "d5", delegatorEmployeeId: "boss", delegateEmployeeId: "me-emp", startDate: day(-1), endDate: day(3), delegatorName: "Patron", delegateName: "Ben" },
];
const ADMIN: Delegation[] = [
  { ...base, id: "a1", delegatorEmployeeId: "x", delegateEmployeeId: "y", startDate: day(-1), endDate: day(3), delegatorName: "Asil A", delegateName: "Vekil A" },
];

const svc = vi.hoisted(() => ({
  listMine: vi.fn(), createMine: vi.fn(), revokeMine: vi.fn(), listAdmin: vi.fn(), createAdmin: vi.fn(), revokeAdmin: vi.fn(),
}));
vi.mock("@/services/workflow-delegations.service", () => ({
  listMyDelegations: svc.listMine,
  createMyDelegation: svc.createMine,
  revokeMyDelegation: svc.revokeMine,
  listDelegations: svc.listAdmin,
  createDelegation: svc.createAdmin,
  revokeDelegation: svc.revokeAdmin,
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
  perms.granted = new Set(["workflow.my.delegation"]);
  for (const m of Object.values(svc)) m.mockReset();
  svc.listMine.mockResolvedValue(MINE);
  svc.listAdmin.mockResolvedValue(ADMIN);
  svc.createMine.mockResolvedValue("new");
  svc.createAdmin.mockResolvedValue("new");
  svc.revokeMine.mockResolvedValue(undefined);
  svc.revokeAdmin.mockResolvedValue(undefined);
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
const submitBtn = () => screen.getByRole("button", { name: trDelegations.form.submit });
const pickOther = () => fireEvent.click(screen.getByText(`pick-other-${trDelegations.form.delegate}`));
const setDates = (f: string, t: string) => {
  fireEvent.change(screen.getByTestId("delegation-from"), { target: { value: f } });
  fireEvent.change(screen.getByTestId("delegation-to"), { target: { value: t } });
};

describe("DelegationsPage", () => {
  it("end_before_start_invalid: blocks saving when the end date precedes the start", async () => {
    renderPage();
    await screen.findByText("Ali");
    pickOther();
    setDates(day(5), day(2));
    fireEvent.click(submitBtn());
    expect(await screen.findByText(trDelegations.errors.endBeforeStart)).toBeTruthy();
    expect(svc.createMine).not.toHaveBeenCalled();
  });

  it("cannot_pick_self: rejects delegating to oneself", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getByText(`pick-self-${trDelegations.form.delegate}`));
    setDates(day(1), day(2));
    fireEvent.click(submitBtn());
    expect(await screen.findByText(trDelegations.errors.self)).toBeTruthy();
    expect(svc.createMine).not.toHaveBeenCalled();
  });

  it("my scope creates on the /mine endpoint with the delegate's employeeId", async () => {
    renderPage();
    await screen.findByText("Ali");
    pickOther();
    setDates(day(1), day(2));
    fireEvent.click(submitBtn());
    await waitFor(() => expect(svc.createMine).toHaveBeenCalledWith({ delegateEmployeeId: "e2", startDate: day(1), endDate: day(2) }));
    expect(svc.createAdmin).not.toHaveBeenCalled();
  });

  it("revoke_calls_service: revoking asks for confirmation, then calls the /mine service", async () => {
    renderPage();
    await screen.findByText("Ali");
    fireEvent.click(screen.getAllByRole("button", { name: trDelegations.list.revoke })[0]);
    expect(svc.revokeMine).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole("button", { name: trDelegations.confirm.action }));
    await waitFor(() => expect(svc.revokeMine).toHaveBeenCalledWith("d1"));
  });

  it("hides revoke for expired, revoked and received delegations", async () => {
    renderPage();
    await screen.findByText("Ali");
    // d1 (active) and d2 (scheduled) only.
    expect(screen.getAllByRole("button", { name: trDelegations.list.revoke })).toHaveLength(2);
  });

  it("active_badge_by_date: status follows dates and the revoked flag", async () => {
    renderPage();
    await screen.findByText("Ali");
    expect(screen.getAllByText(trDelegations.status.active)).toHaveLength(2); // d1 + received d5
    expect(screen.getAllByText(trDelegations.status.scheduled)).toHaveLength(1);
    expect(screen.getAllByText(trDelegations.status.expired)).toHaveLength(1);
    expect(screen.getAllByText(trDelegations.status.revoked)).toHaveLength(1);
  });

  it("shows 'unknown employee' when the API omits names", async () => {
    svc.listMine.mockResolvedValue([{ ...MINE[0], delegateName: undefined }]);
    renderPage();
    expect(await screen.findByText(trDelegations.list.unknown)).toBeTruthy();
  });

  it("admin scope lists and creates on the root endpoints on behalf of another employee", async () => {
    perms.granted = new Set(["workflow.delegation.manage"]);
    renderPage();
    await screen.findByText("Vekil A");
    expect(svc.listMine).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText(`pick-third-${trDelegations.form.delegator}`));
    pickOther();
    setDates(day(1), day(2));
    fireEvent.click(submitBtn());
    await waitFor(() =>
      expect(svc.createAdmin).toHaveBeenCalledWith({ delegatorEmployeeId: "e3", delegateEmployeeId: "e2", startDate: day(1), endDate: day(2) }),
    );
  });

  it("admin scope rejects delegator equal to delegate", async () => {
    perms.granted = new Set(["workflow.delegation.manage"]);
    renderPage();
    await screen.findByText("Vekil A");
    fireEvent.click(screen.getByText(`pick-other-${trDelegations.form.delegator}`));
    pickOther();
    setDates(day(1), day(2));
    fireEvent.click(submitBtn());
    expect(await screen.findByText(trDelegations.errors.self)).toBeTruthy();
    expect(svc.createAdmin).not.toHaveBeenCalled();
  });
});
