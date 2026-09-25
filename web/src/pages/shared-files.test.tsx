import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trDocuments from "../../public/locales/tr/documents.json";
import trCommon from "../../public/locales/tr/common.json";
import SharedFilesPage from "./shared-files";
import {
  classifySharedFile,
  downloadSharedFilesZip,
  listSharedFiles,
  type SharedFile,
} from "@/services/shared-files.service";

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

const TYPE_ID = "t-policy";
const FILES: SharedFile[] = [
  { id: "f1", fileName: "politika.pdf", contentType: "application/pdf", size: 1024, uploadedAt: "2026-09-01T08:00:00Z", uploadedBy: "u1", visibility: "everyone", allowedRoles: [], typeId: TYPE_ID, visibleOnMobile: false },
  { id: "f2", fileName: "rehber.pdf", contentType: "application/pdf", size: 2048, uploadedAt: "2026-09-02T08:00:00Z", uploadedBy: "u1", visibility: "everyone", allowedRoles: [], visibleOnMobile: true },
];

vi.mock("@/services/shared-files.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/shared-files.service")>("@/services/shared-files.service");
  return {
    ...actual,
    listSharedFiles: vi.fn(async () => ({ items: FILES, page: 1, pageSize: 100, totalCount: 2 })),
    getSharedFileQuota: vi.fn(async () => ({ usedBytes: 0, quotaBytes: 1024 * 1024, usedPercent: 0, isExceeded: false })),
    classifySharedFile: vi.fn(async () => undefined),
    downloadSharedFilesZip: vi.fn(async () => new Blob(["zip"])),
  };
});
vi.mock("@/services/shared-file-types.service", () => ({
  listSharedFileTypes: vi.fn(async () => [{ id: "t-policy", name: "Politika", visibleOnMobile: true, isActive: true, sortOrder: 0 }]),
  createSharedFileType: vi.fn(),
  updateSharedFileType: vi.fn(),
  setSharedFileTypeActive: vi.fn(),
}));
vi.mock("@/services/data-transfer.service", () => ({ saveDownloadedFile: vi.fn() }));
vi.mock("@/services/users.service", () => ({ listRoles: vi.fn(async () => []), visibleRoles: () => [] }));

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

beforeEach(() => vi.clearAllMocks());

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "documents",
    ns: ["documents", "common"],
    resources: { tr: { documents: trDocuments, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <SharedFilesPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("SharedFilesPage (E8)", () => {
  it("type_filter", async () => {
    renderPage();
    const user = userEvent.setup();
    await screen.findByText("politika.pdf");

    await user.selectOptions(screen.getByLabelText("Türe göre filtrele"), TYPE_ID);

    await waitFor(() => expect(listSharedFiles).toHaveBeenLastCalledWith(expect.objectContaining({ typeId: TYPE_ID })));
  });

  it("mobile_toggle_saved", async () => {
    renderPage();
    const user = userEvent.setup();
    await screen.findByText("politika.pdf");

    await user.click(screen.getByRole("switch", { name: "politika.pdf mobilde görünsün" }));

    await waitFor(() => expect(classifySharedFile).toHaveBeenCalledWith("f1", { typeId: TYPE_ID, visibleOnMobile: true }));
  });

  it("zip_download_button_uses_selection", async () => {
    renderPage();
    const user = userEvent.setup();
    await screen.findByText("rehber.pdf");

    await user.click(screen.getByRole("checkbox", { name: "rehber.pdf seç" }));
    await user.click(screen.getByRole("button", { name: /ZIP indir \(1\)/ }));

    await waitFor(() => expect(downloadSharedFilesZip).toHaveBeenCalledWith({ fileIds: ["f2"] }));
  });
});
