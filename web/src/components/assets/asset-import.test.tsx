import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAssets from "../../../public/locales/tr/assets.json";
import trCommon from "../../../public/locales/tr/common.json";
import trDataTransfer from "../../../public/locales/tr/dataTransfer.json";
import AssetInventory from "./asset-inventory";
import ColumnMappingStep from "@/components/data-transfer/column-mapping-step";
import DataTransferJobPage from "@/pages/settings/data-transfer-job";
import { toast } from "@/hooks/use-toast";
import {
  downloadImportTemplate,
  getImportJob,
  listImportTemplates,
  runImportJob,
  saveDownloadedFile,
  saveImportJobMapping,
  uploadImportFile,
  type ImportJob,
  type ImportTemplate,
} from "@/services/data-transfer.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));
const IMPORT_PERMISSIONS = ["assets.assets.import", "datatransfer.import.view", "datatransfer.import.run"];

vi.mock("@/components/permission-guard", () => ({
  usePermission: (key: string) => permissions.granted.has(key),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/services/assets.service", async () => ({
  ...await vi.importActual<typeof import("@/services/assets.service")>("@/services/assets.service"),
  listAssets: vi.fn(async () => ({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 })),
  listAssetCategories: vi.fn(async () => []),
  getAssetSummary: vi.fn(async () => ({
    total: 0, unassigned: 0, assigned: 0, pendingAssignment: 0, retired: 0, lost: 0,
    unacknowledged: 0, warrantyExpiring: 0, warrantyExpired: 0, replacementOverdue: 0, categories: [],
  })),
}));
vi.mock("@/services/data-transfer.service", async () => ({
  ...await vi.importActual<typeof import("@/services/data-transfer.service")>("@/services/data-transfer.service"),
  listImportTemplates: vi.fn(), downloadImportTemplate: vi.fn(), uploadImportFile: vi.fn(),
  saveDownloadedFile: vi.fn(), getImportJob: vi.fn(), saveImportJobMapping: vi.fn(), runImportJob: vi.fn(),
}));

const TEMPLATE: ImportTemplate = {
  code: "assets.zimmet", name: "Sunucudaki zimmet şablonu", description: "Sunucudaki açıklama",
  supportedModes: ["Upsert"],
  columns: [{ key: "category", header: "Sunucudaki kategori başlığı", dataType: "Lookup", requirement: "Always", isMatchKey: false, options: null }],
};
const JOB: ImportJob = {
  id: "job-asset", templateCode: "assets.zimmet", fileName: "zimmet.xlsx", status: "Uploaded", mode: "Upsert",
  failurePolicy: "SkipInvalidRows", totalRows: 1, insertedCount: 0, updatedCount: 0, skippedCount: 0, errorCount: 0,
  sourceColumns: [{ index: 0, header: "Kategori", samples: ["LAPTOP"], suggestedTargetKey: "category", autoMatched: true }],
  mapping: [], createdAt: "2026-10-07T09:00:00Z", finishedAt: null,
};

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", { writable: true, value: (query: string) => ({
    matches: false, media: query, onchange: null, addListener() {}, removeListener() {},
    addEventListener() {}, removeEventListener() {}, dispatchEvent: () => false,
  }) });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  Element.prototype.scrollIntoView = () => {};
});
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(listImportTemplates).mockReset();
  vi.mocked(uploadImportFile).mockReset();
  permissions.granted = new Set(IMPORT_PERMISSIONS);
  vi.mocked(listImportTemplates).mockResolvedValue([TEMPLATE, { ...TEMPLATE, code: "employee.basic", name: "Çalışan şablonu" }]);
  vi.mocked(downloadImportTemplate).mockResolvedValue({ blob: new Blob(["template"]), fileName: "zimmet.xlsx" });
  vi.mocked(uploadImportFile).mockResolvedValue(JOB);
  vi.mocked(getImportJob).mockResolvedValue(JOB);
  vi.mocked(saveImportJobMapping).mockResolvedValue({ ...JOB, status: "Mapped" });
  vi.mocked(runImportJob).mockResolvedValue({ ...JOB, status: "Completed", insertedCount: 1, finishedAt: "2026-10-07T09:01:00Z" });
});

function renderFlow(element?: React.ReactNode, initialEntry = "/app/assets") {
  const instance = i18next.createInstance();
  void instance.init({ lng: "tr", fallbackLng: "tr", resources: { tr: { assets: trAssets, common: trCommon, dataTransfer: trDataTransfer } },
    interpolation: { escapeValue: false }, react: { useSuspense: false } });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const view = render(<I18nextProvider i18n={instance}><QueryClientProvider client={client}><MantineProvider>
    <MemoryRouter initialEntries={[initialEntry]}>
      {element ?? <Routes><Route path="/app/assets" element={<AssetInventory />} />
        <Route path="/app/settings/data-transfer/jobs/:id" element={<DataTransferJobPage />} /></Routes>}
    </MemoryRouter>
  </MantineProvider></QueryClientProvider></I18nextProvider>);
  return { ...view, client };
}
async function openImport() {
  await userEvent.click(await screen.findByRole("button", { name: "Excel’den toplu ekle" }));
  return screen.findByRole("dialog");
}

describe("Asset Excel import", () => {
  it("opens for an importer without manage permission and fetches only after opening", async () => {
    renderFlow();
    expect(await screen.findByRole("button", { name: "Excel’den toplu ekle" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Zimmet ekle" })).not.toBeInTheDocument();
    expect(listImportTemplates).not.toHaveBeenCalled();
    const dialog = await openImport();
    expect(await within(dialog).findByText("Sunucudaki kategori başlığı")).toBeInTheDocument();
    expect(within(dialog).queryByText("Çalışan şablonu")).not.toBeInTheDocument();
    expect(within(dialog).getByText(/boşta envanter/i)).toBeInTheDocument();
    expect(listImportTemplates).toHaveBeenCalledWith("tr");
  });
  it.each(IMPORT_PERMISSIONS)("hides import when %s is missing", async (missing) => {
    permissions.granted = new Set([...IMPORT_PERMISSIONS.filter((key) => key !== missing), "assets.assets.manage"]);
    renderFlow();
    await screen.findByRole("button", { name: "Zimmet ekle" });
    expect(screen.queryByRole("button", { name: "Excel’den toplu ekle" })).not.toBeInTheDocument();
    expect(listImportTemplates).not.toHaveBeenCalled();
  });
  it("downloads the server template only with download permission", async () => {
    permissions.granted.add("datatransfer.template.download");
    renderFlow();
    const dialog = await openImport();
    await userEvent.click(await within(dialog).findByRole("button", { name: "Şablon indir" }));
    await waitFor(() => expect(downloadImportTemplate).toHaveBeenCalledWith("assets.zimmet", "tr"));
    expect(vi.mocked(saveDownloadedFile).mock.calls[0]?.[0]).toMatchObject({ fileName: "zimmet.xlsx" });
  });
  it("keeps upload available without template download permission", async () => {
    renderFlow();
    const dialog = await openImport();
    expect(await within(dialog).findByRole("button", { name: "Dosya yükle" })).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Şablon indir" })).not.toBeInTheDocument();
  });
  it.each(["zimmet.xlsx", "zimmet.csv"])("uploads a real %s file with the asset code then opens mapping", async (name) => {
    const user = userEvent.setup();
    renderFlow();
    const dialog = await openImport();
    await within(dialog).findByRole("button", { name: "Dosya yükle" });
    const file = new File(["Kategori\nLAPTOP"], name, { type: name.endsWith("csv") ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
    const input = dialog.querySelector<HTMLInputElement>('input[type="file"]')!;
    await user.upload(input, file);
    await waitFor(() => expect(uploadImportFile).toHaveBeenCalledWith("assets.zimmet", file));
    expect(await screen.findByRole("heading", { name: trDataTransfer.mapping.title })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Zimmet envanterine dön" })).toHaveAttribute("href", "/app/assets");
  });
  it("rejects an unsupported file and an oversized file before upload", async () => {
    const user = userEvent.setup({ applyAccept: false });
    renderFlow();
    const dialog = await openImport();
    await within(dialog).findByRole("button", { name: "Dosya yükle" });
    const input = dialog.querySelector<HTMLInputElement>('input[type="file"]')!;
    await user.upload(input, new File(["text"], "zimmet.txt", { type: "text/plain" }));
    await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: trDataTransfer.upload.invalidType })));
    await user.upload(input, new File([new Uint8Array(10 * 1024 * 1024 + 1)], "large.csv", { type: "text/csv" }));
    await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: trDataTransfer.upload.tooLarge })));
    expect(uploadImportFile).not.toHaveBeenCalled();
  });
  it("shows loading then allows retry after a template request failure", async () => {
    let rejectRequest!: (reason: Error) => void;
    vi.mocked(listImportTemplates).mockImplementationOnce(() => new Promise((_, reject) => { rejectRequest = reject; }));
    renderFlow();
    const dialog = await openImport();
    expect(within(dialog).getByRole("status")).toBeInTheDocument();
    rejectRequest(new Error("offline"));
    await userEvent.click(await within(dialog).findByRole("button", { name: "Tekrar dene" }));
    expect(await within(dialog).findByText("Sunucudaki kategori başlığı")).toBeInTheDocument();
    expect(listImportTemplates).toHaveBeenCalledTimes(2);
  });
  it("explains when the asset template is unavailable and offers no upload", async () => {
    vi.mocked(listImportTemplates).mockResolvedValue([]);
    renderFlow();
    const dialog = await openImport();
    expect(await within(dialog).findByText(/Zimmet aktarım şablonu kullanılamıyor/)).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Dosya yükle" })).not.toBeInTheDocument();
  });
  it("lets the user retry a failed upload using the same file", async () => {
    vi.mocked(uploadImportFile).mockRejectedValueOnce(new Error("offline"));
    const user = userEvent.setup();
    renderFlow();
    const dialog = await openImport();
    await within(dialog).findByRole("button", { name: "Dosya yükle" });
    const input = dialog.querySelector<HTMLInputElement>('input[type="file"]')!;
    const file = new File(["LAPTOP"], "zimmet.csv", { type: "text/csv" });
    await user.upload(input, file);
    await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: trDataTransfer.upload.error })));
    await user.click(within(dialog).getByRole("button", { name: "Dosya yükle" }));
    await user.upload(input, file);
    expect(await screen.findByRole("heading", { name: trDataTransfer.mapping.title })).toBeInTheDocument();
  });
});

describe("Asset import completion", () => {
  it.each(["assets.zimmet", "employee.basic"])("invalidates asset queries only after a successful %s run", async (templateCode) => {
    const job = { ...JOB, templateCode };
    const template = { ...TEMPLATE, code: templateCode };
    const { client } = renderFlow(<ColumnMappingStep job={job} template={template} />);
    client.setQueryData(["assets", "inventory"], { items: [] });
    await userEvent.click(screen.getByRole("button", { name: trDataTransfer.mapping.saveAndContinue }));
    await waitFor(() => expect(runImportJob).toHaveBeenCalledWith("job-asset"));
    await waitFor(() => expect(client.getQueryState(["datatransfer", "import-job", "job-asset"])?.data).toMatchObject({ status: "Completed" }));
    expect(client.getQueryState(["assets", "inventory"])?.isInvalidated).toBe(templateCode === "assets.zimmet");
  });
  it("keeps the history return link for other templates", async () => {
    vi.mocked(getImportJob).mockResolvedValue({ ...JOB, templateCode: "employee.basic" });
    renderFlow(undefined, "/app/settings/data-transfer/jobs/job-asset");
    await screen.findByRole("heading", { name: trDataTransfer.mapping.title });
    expect(screen.queryByRole("link", { name: "Zimmet envanterine dön" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: trDataTransfer.job.back })).toHaveAttribute("href", "/app/settings/data-transfer?tab=history");
  });
});
