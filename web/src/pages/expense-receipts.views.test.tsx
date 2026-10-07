import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import { apiClient } from "@senseik/api-client";
import PaymentsPage from "./payments";
import WorkflowInboxPage from "./inbox";
import ExpensePaymentsAdminPage from "./expense-payments-admin";
import trExpense from "../../public/locales/tr/expense.json";
import trCommon from "../../public/locales/tr/common.json";
import trWorkflow from "../../public/locales/tr/workflow.json";
import trLeave from "../../public/locales/tr/leave.json";

vi.mock("@senseik/api-client", () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));
vi.mock("@/components/permission-guard", () => ({ usePermission: (permission: string) => !["expense.claims.pay", "expense.advances.view", "workflow.request.override", "documents.my.files"].includes(permission) }));
const claim = {
  id: "claim", employeeId: "employee", status: "approved", description: "İş yemeği", totalAmount: 450,
  items: [{ id: "item", receiptDate: "2026-10-07", amount: 450, currency: "TRY", description: "Restoran", receiptAttachments: [
    { documentId: "first", fileName: "fis.pdf", contentType: "application/pdf", size: 1024 },
    { documentId: "second", fileName: "fatura.png", contentType: "image/png", size: 2048 },
  ] }],
};
beforeAll(() => {
  Object.defineProperty(document, "fonts", { value: { addEventListener() {}, removeEventListener() {} } });
  Object.defineProperty(window, "matchMedia", { value: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }) });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
});
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.get).mockImplementation(async url => {
    const path = String(url);
    if (path.endsWith("/inbox")) return { data: [{ taskId: "task", approvalRequestId: "approval", requestType: "ExpenseClaim", subjectId: "claim", requesterName: "Ayşe Yılmaz", stepNumber: 1, isFallbackRoleTask: false }] };
    if (path.endsWith("/payments/my")) return { data: [{ kind: "expense", sourceId: "claim", date: "2026-10-07", amount: 450, currency: "TRY", status: "approved", description: "İş yemeği" }] };
    if (path.endsWith("/claims")) return { data: [claim] };
    return { data: claim };
  });
});
async function renderPage(Page: typeof PaymentsPage) {
  const i18n = i18next.createInstance();
  await i18n.init({ lng: "tr", resources: { tr: { expense: trExpense, common: trCommon, workflow: trWorkflow, leave: trLeave } }, interpolation: { escapeValue: false } });
  render(<I18nextProvider i18n={i18n}><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}><MantineProvider><MemoryRouter><Page /></MemoryRouter></MantineProvider></QueryClientProvider></I18nextProvider>);
}
describe("expense receipts in each authorized view", () => {
  it("opens the requester claim details from its payment row", async () => {
    await renderPage(PaymentsPage);
    await userEvent.click(await screen.findByRole("button", { name: "Belgeleri ve detayı gör" }));
    await screen.findByText("fis.pdf");
    expect(screen.getByText("fatura.png")).toBeInTheDocument();
    expect(apiClient.get).toHaveBeenCalledWith("/v1/expense/claims/claim");
  });
  it("shows all documents in the approval dialog while retaining the approval action", async () => {
    await renderPage(WorkflowInboxPage);
    await userEvent.click(await screen.findByRole("button", { name: "Karar ver" }));
    await screen.findByText("fis.pdf");
    expect(screen.getByText("fatura.png")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Onayla" })).toBeEnabled();
    expect(apiClient.get).toHaveBeenCalledWith("/v1/expense/claims/claim/review");
  });
  it("lets HR viewers inspect documents without gaining payment permission", async () => {
    await renderPage(ExpensePaymentsAdminPage);
    await userEvent.click(await screen.findByRole("button", { name: "Belgeleri ve detayı gör" }));
    await screen.findByText("fis.pdf");
    expect(screen.getByText("fatura.png")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Öde" })).not.toBeInTheDocument();
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith("/v1/expense/claims/claim/review"));
  });
});
