import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import PaymentsPage from "./payments";
import trExpense from "../../public/locales/tr/expense.json";
import trCommon from "../../public/locales/tr/common.json";

const mocks = vi.hoisted(() => ({ upload: vi.fn(), add: vi.fn(), submit: vi.fn() }));
vi.mock("@/components/permission-guard", () => ({ usePermission: () => true }));
vi.mock("@/services/documents.service", () => ({ uploadDocumentFile: mocks.upload }));
vi.mock("@/services/expense.service", async () => ({
  ...await vi.importActual("@/services/expense.service"),
  listMyPayments: async () => [],
  listExpenseCategories: async () => [{ id: "food", code: "food", nameKey: "food", isActive: true }],
  createExpenseClaim: async () => "claim",
  getExpenseClaim: async () => ({ id: "claim", items: [{ id: "existing", description: "Önceki harcama", receiptDate: "2026-10-07", amount: 20, currency: "TRY", receiptAttachments: [] }], totalAmount: 20 }),
  addExpenseItem: mocks.add,
  submitExpenseClaim: mocks.submit,
}));

beforeAll(() => {
  Object.defineProperty(document, "fonts", { value: { addEventListener() {}, removeEventListener() {} } });
  Object.defineProperty(window, "matchMedia", { writable: true, value: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }) });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
});
beforeEach(() => {
  vi.clearAllMocks();
  mocks.upload.mockImplementation(async (file: File) => `id-${file.name}`);
  mocks.add.mockResolvedValue("item");
});

async function setup() {
  const i18n = i18next.createInstance();
  await i18n.init({ lng: "tr", resources: { tr: { expense: trExpense, common: trCommon } }, interpolation: { escapeValue: false } });
  render(<I18nextProvider i18n={i18n}><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}><MantineProvider><MemoryRouter><PaymentsPage /></MemoryRouter></MantineProvider></QueryClientProvider></I18nextProvider>);
  fireEvent.click(screen.getByRole("button", { name: "+ Harcama" }));
  fireEvent.click(screen.getByRole("button", { name: "Devam et" }));
  const category = await screen.findByPlaceholderText("Kategori seçin");
  await userEvent.click(category);
  await userEvent.click(await screen.findByText("Yiyecek ve İçecek"));
  fireEvent.change(screen.getByLabelText("Tutar"), { target: { value: "450" } });
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  expect(input, "receipt file selection is available").not.toBeNull();
  return input!;
}
const files = () => [new File(["pdf"], "fis.pdf", { type: "application/pdf" }), new File(["png"], "fatura.png", { type: "image/png" })];
const add = () => fireEvent.click(screen.getByRole("button", { name: "Harcama kalemi ekle" }));

describe("expense receipt attachments", () => {
  it("uploads two documents then creates exactly one item with its original amount", async () => {
    fireEvent.change(await setup(), { target: { files: files() } });
    add();
    await waitFor(() => expect(mocks.add).toHaveBeenCalledWith("claim", expect.objectContaining({ amount: 450, receiptFileIds: ["id-fis.pdf", "id-fatura.png"] })));
    expect(mocks.add).toHaveBeenCalledTimes(1);
    expect(mocks.upload).toHaveBeenCalledWith(expect.any(File), "documents", "me");
  });
  it("removes one staged file before uploading without deleting documents", async () => {
    fireEvent.change(await setup(), { target: { files: files() } });
    fireEvent.click(screen.getByRole("button", { name: "fis.pdf belgesini kaldır" }));
    add();
    await waitFor(() => expect(mocks.add).toHaveBeenCalledWith("claim", expect.objectContaining({ receiptFileIds: ["id-fatura.png"] })));
    expect(mocks.upload).toHaveBeenCalledTimes(1);
  });
  it("retains successful uploads and retries only the failed file", async () => {
    mocks.upload.mockResolvedValueOnce("first").mockRejectedValueOnce(new Error("storage unavailable")).mockResolvedValueOnce("second");
    fireEvent.change(await setup(), { target: { files: files() } });
    add();
    await screen.findByText(/Yüklenemedi; tekrar deneyebilirsiniz\./);
    expect(mocks.add).not.toHaveBeenCalled();
    expect(screen.getByText("fis.pdf")).toBeInTheDocument();
    add();
    await waitFor(() => expect(mocks.add).toHaveBeenCalledWith("claim", expect.objectContaining({ receiptFileIds: ["first", "second"] })));
    expect(mocks.upload).toHaveBeenCalledTimes(3);
  });
  it("retains files and IDs after an item POST fails without automatically retrying it", async () => {
    mocks.add.mockRejectedValueOnce(new Error("network lost"));
    fireEvent.change(await setup(), { target: { files: files() } });
    add();
    await screen.findByText(/Kalem eklenemedi/);
    expect(mocks.add).toHaveBeenCalledTimes(1);
    expect(screen.getByText("fis.pdf")).toBeInTheDocument();
    expect(screen.getByLabelText("Tutar")).toHaveValue("450 TRY");
  });
  it("blocks adding, submitting, removing and closing while uploads are in flight", async () => {
    let resolve!: (id: string) => void;
    mocks.upload.mockImplementationOnce(() => new Promise<string>(r => { resolve = r; }));
    fireEvent.change(await setup(), { target: { files: [files()[0]] } });
    add();
    await waitFor(() => expect(mocks.upload).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("button", { name: "Gönder" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "fis.pdf belgesini kaldır" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Kapat" })).toBeDisabled();
    expect(within(screen.getByRole("dialog")).getByRole("button", { name: "Sil" })).toBeDisabled();
    resolve("first");
    await waitFor(() => expect(mocks.add).toHaveBeenCalledTimes(1));
  });
  it("rejects unsupported images before making an upload or item request", async () => {
    fireEvent.change(await setup(), { target: { files: [new File(["<svg/>"], "fis.svg", { type: "image/svg+xml" })] } });
    await screen.findByText(/PDF, JPEG, PNG, GIF veya WebP/);
    expect(mocks.upload).not.toHaveBeenCalled();
    expect(mocks.add).not.toHaveBeenCalled();
  });
  it("rejects a document larger than 100 MB before uploading", async () => {
    const oversized = files()[0];
    Object.defineProperty(oversized, "size", { value: 104857601 });
    fireEvent.change(await setup(), { target: { files: [oversized] } });
    await screen.findByText(/Her belge 100 MB/);
    expect(mocks.upload).not.toHaveBeenCalled();
    expect(screen.queryByText("fis.pdf")).not.toBeInTheDocument();
  });
});
