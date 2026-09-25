import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Group, Modal, Radio, Stack, Table, Tabs, Text, TextInput } from "@mantine/core";
import { Download } from "lucide-react";
import { FileDropzone } from "@/components/ui/file-dropzone";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { parseDateOnly } from "@/lib/dates";
import type { CalendarShiftAssignment } from "@/services/attendance-shift-calendar.service";
import {
  SHIFT_PLAN_IMPORT_TEMPLATE_CODE,
  applyShiftTemplate,
  createShiftTemplate,
  listShiftTemplates,
  type ShiftTemplateEntry,
} from "@/services/attendance-shift-templates.service";
import {
  IMPORT_ALLOWED_EXTENSIONS,
  IMPORT_MAX_FILE_BYTES,
  downloadImportTemplate,
  listImportJobRows,
  runImportJob,
  saveDownloadedFile,
  saveImportJobMapping,
  uploadImportFile,
  type ImportJob,
  type ImportJobRow,
} from "@/services/data-transfer.service";

const TEMPLATES_KEY = ["attendance", "shift-templates"] as const;
const ERROR_ROWS_PAGE_SIZE = 50;
const MS_PER_DAY = 86_400_000;

interface ShiftTemplateDialogProps {
  opened: boolean;
  onClose: () => void;
  /** Seçili hücreler (`employeeId|date`). */
  selectedKeys: ReadonlySet<string>;
  /** Görünen haftanın atamaları; seçimdeki dolu hücreler şablona girer. */
  assignments: readonly CalendarShiftAssignment[];
  /** Uygulama başlangıç tarihi önerisi (yyyy-MM-dd). */
  weekStart: string;
  /** Uygulama / içe aktarma sonrası takvimi tazelemek için. */
  onChanged?: () => void;
}

const dayDiff = (from: string, to: string) => Math.round((parseDateOnly(to).getTime() - parseDateOnly(from).getTime()) / MS_PER_DAY);

/** Seçimdeki atanmış hücrelerden şablon girdileri + dönem uzunluğu (seçim kutusunun en erken gününden başlar). */
export function buildTemplateFromSelection(selectedKeys: ReadonlySet<string>, assignments: readonly CalendarShiftAssignment[]) {
  const cells = [...selectedKeys].map((key) => {
    const [employeeId, date] = key.split("|");
    return { employeeId, date };
  });
  if (cells.length === 0) return { entries: [] as ShiftTemplateEntry[], periodLengthDays: 0 };
  const dates = cells.map((c) => c.date).sort();
  const first = dates[0];
  const periodLengthDays = dayDiff(first, dates[dates.length - 1]) + 1;
  const byCell = new Map(assignments.map((a) => [`${a.employeeId}|${a.date}`, a]));
  const entries: ShiftTemplateEntry[] = [];
  for (const cell of cells) {
    const assignment = byCell.get(`${cell.employeeId}|${cell.date}`);
    if (!assignment) continue;
    const dayType = assignment.dayType ?? "work";
    if (dayType === "work" && !assignment.shiftDefinitionId) continue;
    entries.push({
      employeeId: cell.employeeId,
      dayOffset: dayDiff(first, cell.date),
      shiftDefinitionId: dayType === "work" ? assignment.shiftDefinitionId : null,
      dayType,
    });
  }
  entries.sort((a, b) => a.employeeId.localeCompare(b.employeeId) || a.dayOffset - b.dayOffset);
  return { entries, periodLengthDays };
}

export default function ShiftTemplateDialog({ opened, onClose, selectedKeys, assignments, weekStart, onChanged }: ShiftTemplateDialogProps) {
  const { t } = useTranslation(["attendance"]);
  return (
    <Modal opened={opened} onClose={onClose} title={t("attendance:shiftTemplates.title")} size="lg">
      <Tabs defaultValue="save">
        <Tabs.List>
          <Tabs.Tab value="save">{t("attendance:shiftTemplates.tabSave")}</Tabs.Tab>
          <Tabs.Tab value="apply">{t("attendance:shiftTemplates.tabApply")}</Tabs.Tab>
          <Tabs.Tab value="import">{t("attendance:shiftTemplates.tabImport")}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="save" pt="md">
          <SaveTab selectedKeys={selectedKeys} assignments={assignments} />
        </Tabs.Panel>
        <Tabs.Panel value="apply" pt="md">
          <ApplyTab weekStart={weekStart} onChanged={onChanged} />
        </Tabs.Panel>
        <Tabs.Panel value="import" pt="md">
          <ImportTab onChanged={onChanged} />
        </Tabs.Panel>
      </Tabs>
    </Modal>
  );
}

function useErrorToast() {
  const { t } = useTranslation(["attendance", "common"]);
  return (err: unknown) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("attendance:genericError")) });
}

function SaveTab({ selectedKeys, assignments }: Pick<ShiftTemplateDialogProps, "selectedKeys" | "assignments">) {
  const { t } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const onError = useErrorToast();
  const [name, setName] = useState("");
  const built = useMemo(() => buildTemplateFromSelection(selectedKeys, assignments), [selectedKeys, assignments]);

  const mutation = useMutation({
    mutationFn: () => createShiftTemplate({ name: name.trim(), periodLengthDays: built.periodLengthDays, entries: built.entries }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftTemplates.saved") });
      setName("");
    },
    onError,
  });

  const empty = built.entries.length === 0;
  return (
    <Stack gap="sm">
      <Text size="sm" c="dimmed">
        {empty
          ? t("attendance:shiftTemplates.selectionEmpty")
          : t("attendance:shiftTemplates.selectionSummary", { entries: built.entries.length, days: built.periodLengthDays })}
      </Text>
      <TextInput label={t("attendance:shiftTemplates.name")} value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={100} disabled={empty} />
      <Group justify="flex-end">
        <Button onClick={() => mutation.mutate()} disabled={empty || name.trim() === ""} loading={mutation.isPending}>
          {t("attendance:shiftTemplates.save")}
        </Button>
      </Group>
    </Stack>
  );
}

function ApplyTab({ weekStart, onChanged }: Pick<ShiftTemplateDialogProps, "weekStart" | "onChanged">) {
  const { t } = useTranslation(["attendance", "common"]);
  const onError = useErrorToast();
  const [templateId, setTemplateId] = useState<string | null>(null);
  const [startDate, setStartDate] = useState(weekStart);
  const [overwritePublished, setOverwritePublished] = useState(false);
  const templatesQuery = useQuery({ queryKey: TEMPLATES_KEY, queryFn: listShiftTemplates });

  const mutation = useMutation({
    mutationFn: () => applyShiftTemplate(templateId as string, { startDate, overwritePublished }),
    onSuccess: (result) => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance:shiftTemplates.applied", { count: result.affectedCount }) });
      if (result.skipped.length > 0) {
        toast({ variant: "default", description: t("attendance:shiftPlan.bulkSkipped", { count: result.skipped.length }) });
      }
      onChanged?.();
    },
    onError,
  });

  const templates = templatesQuery.data ?? [];
  return (
    <Stack gap="sm">
      {templatesQuery.isSuccess && templates.length === 0 && <Text size="sm" c="dimmed">{t("attendance:shiftTemplates.noTemplates")}</Text>}
      <Radio.Group value={templateId} onChange={setTemplateId} label={t("attendance:shiftTemplates.pick")}>
        <Stack gap="xs" mt="xs">
          {templates.map((tpl) => (
            <Radio key={tpl.id} value={tpl.id} label={`${tpl.name} (${t("attendance:shiftTemplates.days", { count: tpl.periodLengthDays })})`} />
          ))}
        </Stack>
      </Radio.Group>
      <TextInput type="date" label={t("attendance:shiftTemplates.startDate")} value={startDate} onChange={(e) => setStartDate(e.currentTarget.value)} />
      <Radio.Group value={overwritePublished ? "yes" : "no"} onChange={(v) => setOverwritePublished(v === "yes")}>
        <Group gap="md">
          <Radio value="no" label={t("attendance:shiftTemplates.keepPublished")} />
          <Radio value="yes" label={t("attendance:shiftTemplates.overwritePublished")} />
        </Group>
      </Radio.Group>
      <Group justify="flex-end">
        <Button onClick={() => mutation.mutate()} disabled={!templateId || !startDate} loading={mutation.isPending}>
          {t("attendance:shiftTemplates.apply")}
        </Button>
      </Group>
    </Stack>
  );
}

interface ImportOutcome {
  job: ImportJob;
  errorRows: ImportJobRow[];
}

function ImportTab({ onChanged }: Pick<ShiftTemplateDialogProps, "onChanged">) {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const onError = useErrorToast();
  const [outcome, setOutcome] = useState<ImportOutcome | null>(null);

  const mutation = useMutation({
    /** Yükle → önerilen eşleme → çalıştır → hatalı satırları oku (DataTransfer akışı, sihirbazsız). */
    mutationFn: async (file: File): Promise<ImportOutcome> => {
      const uploaded = await uploadImportFile(SHIFT_PLAN_IMPORT_TEMPLATE_CODE, file);
      await saveImportJobMapping(uploaded.id, {
        mapping: uploaded.sourceColumns.map((c) => ({ sourceIndex: c.index, targetKey: c.suggestedTargetKey })),
        mode: uploaded.mode ?? "Upsert",
        failurePolicy: uploaded.failurePolicy ?? "SkipInvalidRows",
      });
      const job = await runImportJob(uploaded.id);
      const rows = job.errorCount > 0 ? await listImportJobRows(job.id, { outcome: "Error", page: 1, pageSize: ERROR_ROWS_PAGE_SIZE }) : null;
      return { job, errorRows: rows?.items ?? [] };
    },
    onSuccess: (result) => {
      setOutcome(result);
      onChanged?.();
    },
    onError: (err) => {
      setOutcome(null);
      onError(err);
    },
  });

  async function downloadTemplate() {
    try {
      saveDownloadedFile(await downloadImportTemplate(SHIFT_PLAN_IMPORT_TEMPLATE_CODE, i18n.language.slice(0, 2)));
    } catch (err) {
      onError(err);
    }
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <Text size="sm" c="dimmed">{t("attendance:shiftTemplates.importHint")}</Text>
        <Button variant="default" size="xs" leftSection={<Download size={14} />} onClick={downloadTemplate}>
          {t("attendance:shiftTemplates.downloadTemplate")}
        </Button>
      </Group>
      <FileDropzone
        accept={IMPORT_ALLOWED_EXTENSIONS.join(",")}
        maxBytes={IMPORT_MAX_FILE_BYTES}
        busy={mutation.isPending}
        title={t("attendance:shiftTemplates.dropzoneTitle")}
        busyLabel={t("attendance:shiftTemplates.importing")}
        onFile={(file) => mutation.mutate(file)}
        onReject={() => onError(new Error(t("attendance:shiftTemplates.invalidFile")))}
      />
      {outcome && (
        <Stack gap="xs">
          <Text size="sm">
            {t("attendance:shiftTemplates.importSummary", {
              inserted: outcome.job.insertedCount + outcome.job.updatedCount,
              errors: outcome.job.errorCount,
            })}
          </Text>
          {outcome.errorRows.length > 0 && (
            <>
              <Alert color="yellow" variant="light">{t("attendance:shiftTemplates.errorsHint")}</Alert>
              <Table withTableBorder fz="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("attendance:shiftTemplates.colRow")}</Table.Th>
                    <Table.Th>{t("attendance:shiftTemplates.colKey")}</Table.Th>
                    <Table.Th>{t("attendance:shiftTemplates.colError")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {outcome.errorRows.map((row) => (
                    <Table.Tr key={row.rowNumber}>
                      <Table.Td>{row.rowNumber}</Table.Td>
                      <Table.Td>{row.matchKey ?? "—"}</Table.Td>
                      <Table.Td>
                        {row.errors.map((e, i) => (
                          <div key={i}>{e.message}</div>
                        ))}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </>
          )}
        </Stack>
      )}
    </Stack>
  );
}
