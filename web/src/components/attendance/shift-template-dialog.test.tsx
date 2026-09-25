import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import ShiftTemplateDialog from "./shift-template-dialog";
import { applyShiftTemplate, createShiftTemplate, listShiftTemplates } from "@/services/attendance-shift-templates.service";
import { getImportJob, listImportJobRows, runImportJob, saveImportJobMapping, uploadImportFile } from "@/services/data-transfer.service";
import type { CalendarShiftAssignment } from "@/services/attendance-shift-calendar.service";

vi.mock("@/services/attendance-shift-templates.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance-shift-templates.service")>("@/services/attendance-shift-templates.service");
  return { ...actual, listShiftTemplates: vi.fn(), createShiftTemplate: vi.fn(), applyShiftTemplate: vi.fn() };
});
vi.mock("@/services/data-transfer.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/data-transfer.service")>("@/services/data-transfer.service");
  return {
    ...actual,
    uploadImportFile: vi.fn(),
    saveImportJobMapping: vi.fn(),
    runImportJob: vi.fn(),
    listImportJobRows: vi.fn(),
    getImportJob: vi.fn(),
    downloadImportTemplate: vi.fn(),
    saveDownloadedFile: vi.fn(),
  };
});
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {}, dispatchEvent: () => false }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

const assignments = [
  { id: "a1", employeeId: "e1", date: "2026-09-21", shiftDefinitionId: "d1", dayType: "work" },
  { id: "a2", employeeId: "e1", date: "2026-09-23", dayType: "weeklyRest" },
  { id: "a3", employeeId: "e2", date: "2026-09-21", shiftDefinitionId: "d1", dayType: "work" },
] as unknown as CalendarShiftAssignment[];

function renderDialog(selected: string[]) {
  const i18n = i18next.createInstance();
  void i18n.init({ lng: "tr", resources: { tr: { attendance: trAttendance, common: trCommon } }, ns: ["attendance", "common"], defaultNS: "attendance" });
  const onClose = vi.fn();
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MantineProvider>
          <ShiftTemplateDialog opened onClose={onClose} selectedKeys={new Set(selected)} assignments={assignments} weekStart="2026-09-21" />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { onClose };
}

describe("ShiftTemplateDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listShiftTemplates).mockResolvedValue([{ id: "t1", name: "Yaz planı", periodLengthDays: 7, entries: [] }]);
  });

  it("save_template_from_selection", async () => {
    vi.mocked(createShiftTemplate).mockResolvedValue({ id: "t2", name: "X", periodLengthDays: 3, entries: [] });
    const user = userEvent.setup();
    renderDialog(["e1|2026-09-21", "e1|2026-09-23", "e2|2026-09-21", "e2|2026-09-22"]);
    await user.type(await screen.findByLabelText("Şablon adı"), "Hafta A");
    await user.click(screen.getByRole("button", { name: "Şablonu kaydet" }));
    await waitFor(() => expect(createShiftTemplate).toHaveBeenCalled());
    expect(createShiftTemplate).toHaveBeenCalledWith({
      name: "Hafta A",
      periodLengthDays: 3,
      entries: [
        { employeeId: "e1", dayOffset: 0, shiftDefinitionId: "d1", dayType: "work" },
        { employeeId: "e1", dayOffset: 2, shiftDefinitionId: null, dayType: "weeklyRest" },
        { employeeId: "e2", dayOffset: 0, shiftDefinitionId: "d1", dayType: "work" },
      ],
    });
  });

  it("apply_template_to_range", async () => {
    vi.mocked(applyShiftTemplate).mockResolvedValue({ affectedCount: 5, skipped: [] });
    const user = userEvent.setup();
    renderDialog([]);
    await user.click(screen.getByRole("tab", { name: "Şablon uygula" }));
    await user.click(await screen.findByRole("radio", { name: /Yaz planı/ }));
    await user.click(screen.getByRole("button", { name: "Uygula" }));
    await waitFor(() => expect(applyShiftTemplate).toHaveBeenCalledWith("t1", { startDate: "2026-09-21", overwritePublished: false }));
  });

  it("excel_import_shows_row_errors", async () => {
    vi.mocked(uploadImportFile).mockResolvedValue({
      id: "j1", templateCode: "shift-plan", fileName: "p.xlsx", status: "Uploaded", mode: "Upsert", failurePolicy: "SkipInvalidRows",
      totalRows: 2, insertedCount: 0, updatedCount: 0, skippedCount: 0, errorCount: 0, createdAt: "", finishedAt: null, mapping: [],
      sourceColumns: [{ index: 0, header: "Sicil", samples: [], suggestedTargetKey: "employeeNo", autoMatched: true }],
    });
    vi.mocked(saveImportJobMapping).mockResolvedValue({} as never);
    vi.mocked(runImportJob).mockResolvedValue({
      id: "j1", templateCode: "shift-plan", fileName: "p.xlsx", status: "CompletedWithErrors", mode: "Upsert", failurePolicy: "SkipInvalidRows",
      totalRows: 2, insertedCount: 1, updatedCount: 0, skippedCount: 0, errorCount: 1, createdAt: "", finishedAt: "", mapping: [], sourceColumns: [],
    });
    vi.mocked(getImportJob).mockResolvedValue({} as never);
    vi.mocked(listImportJobRows).mockResolvedValue({
      items: [{ rowNumber: 3, outcome: "Error", matchKey: "E-9", errors: [{ column: "Vardiya", code: "shift.unknown", message: "Bilinmeyen vardiya" }] }],
      totalCount: 1, page: 1, pageSize: 50,
    } as never);
    const user = userEvent.setup();
    renderDialog([]);
    await user.click(screen.getByRole("tab", { name: "Excel içe aktar" }));
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(["x"], "p.xlsx"));
    expect(await screen.findByText("Bilinmeyen vardiya")).toBeInTheDocument();
    expect(screen.getByText("E-9")).toBeInTheDocument();
    expect(uploadImportFile).toHaveBeenCalledWith("shift-plan", expect.any(File));
    expect(saveImportJobMapping).toHaveBeenCalledWith("j1", { mapping: [{ sourceIndex: 0, targetKey: "employeeNo" }], mode: "Upsert", failurePolicy: "SkipInvalidRows" });
    expect(listImportJobRows).toHaveBeenCalledWith("j1", { outcome: "Error", page: 1, pageSize: 50 });
  });
});
