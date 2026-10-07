import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { apiClient } from "@senseik/api-client";
import { ExpenseClaimDetail } from "./expense-claim-detail";
import trExpense from "../../../public/locales/tr/expense.json";
import trCommon from "../../../public/locales/tr/common.json";

vi.mock("@senseik/api-client", () => ({ apiClient: { get: vi.fn() } }));
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", { value: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }) });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
});
async function setup(review = false) {
  const i18n = i18next.createInstance();
  await i18n.init({ lng: "tr", resources: { tr: { expense: trExpense, common: trCommon } } });
  vi.mocked(apiClient.get).mockImplementation(async (url) => ({ data: String(url).endsWith("download-url") ? { url: "https://storage.test/receipt", expiresAt: "2026-10-07", fileName: "fis.pdf", contentType: "application/pdf" } : { id: "claim", totalAmount: 450, items: [{ id: "item", receiptDate: "2026-10-07", amount: 450, currency: "TRY", description: "İş yemeği", receiptAttachments: [{ documentId: "doc1", fileName: "fis.pdf", contentType: "application/pdf", size: 1024 }, { documentId: "doc2", fileName: "fatura.png", contentType: "image/png", size: 2048 }] }] } }));
  render(<I18nextProvider i18n={i18n}><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MantineProvider><ExpenseClaimDetail claimId="claim" review={review} /></MantineProvider></QueryClientProvider></I18nextProvider>);
}
describe("saved expense receipts", () => {
  it("shows every receipt and downloads through the protected expense item endpoint", async () => {
    const open = vi.spyOn(window, "open").mockImplementation(() => null);
    await setup();
    await screen.findByText("fis.pdf");
    expect(screen.getByText("fatura.png")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "fis.pdf belgesini aç" }));
    await waitFor(() => expect(open).toHaveBeenCalledWith("https://storage.test/receipt", "_blank", "noopener,noreferrer"));
    expect(apiClient.get).toHaveBeenCalledWith("/v1/expense/claims/claim/items/item/attachments/doc1/download-url");
    open.mockRestore();
  });
  it("loads approval and HR details via the authorized review endpoint", async () => {
    await setup(true);
    await screen.findByText("fis.pdf");
    expect(apiClient.get).toHaveBeenCalledWith("/v1/expense/claims/claim/review");
  });
});
