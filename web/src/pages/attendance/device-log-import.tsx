import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Alert, Button, Card, Group, SimpleGrid, Stack, Text, Title } from "@mantine/core";
import { Download, FileWarning, Upload } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { FileDropzone, type FileRejectReason } from "@/components/ui/file-dropzone";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { saveDownloadedFile } from "@/services/data-transfer.service";
import { importDeviceLogs, type DeviceLogImportError, type DeviceLogImportResult } from "@/services/attendance.service";

const MAX_BYTES = 5 * 1024 * 1024; // 5 MB — aşımda API 400 `attendance.device_log.file_too_large` döner.
// employeeCode = çalışanın TC kimlik numarası (ImportDeviceLogsHandler `FindActiveByNationalIdAsync` ile eşleştiriyor).
const SAMPLE_CSV = "employeeCode;timestamp;direction;deviceId\r\n10000000146;2026-09-16T08:03:00;in;TURNSTILE-1\r\n10000000146;2026-09-16T17:05:00;out;TURNSTILE-1\r\n";

/**
 * PDKS cihaz kaydı CSV içe aktarımı (S-11, ilk sürüm: yalnız elle yükleme — canlı SFTP/cihaz entegrasyonu
 * K-12 ile ertelendi). Bilinmeyen çalışan kodu / bozuk tarih / tekrar eden kayıt satır bazında atlanır,
 * dosyanın tamamı reddedilmez (`POST attendance/device-logs/import`, attendance.timesheet.import).
 */
export default function DeviceLogImportPage() {
  const canImport = usePermission("attendance.timesheet.import");
  if (!canImport) return <NoAccess />;
  return <DeviceLogImport />;
}

function DeviceLogImport() {
  const { t } = useTranslation(["attendance", "common"]);
  const [result, setResult] = useState<DeviceLogImportResult | null>(null);

  const mutation = useMutation({
    mutationFn: importDeviceLogs,
    onSuccess: (data) => {
      setResult(data);
      toast({
        variant: data.errors.length > 0 ? "default" : "success",
        title: t("common:success"),
        description: t("attendance:deviceLogImport.resultSummary", { imported: data.imported, skipped: data.skipped }),
      });
    },
    onError: (error) => {
      setResult(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("attendance:genericError")) });
    },
  });

  function downloadSample() {
    saveDownloadedFile({ blob: new Blob([SAMPLE_CSV], { type: "text/csv;charset=utf-8;" }), fileName: "pdks-ornek.csv" });
  }

  function handleReject(reason: FileRejectReason) {
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: reason === "size" ? t("attendance:deviceLogImport.fileTooLarge") : t("attendance:deviceLogImport.invalidType"),
    });
  }

  const columns: ColumnDef<DeviceLogImportError>[] = [
    {
      id: "line",
      header: t("attendance:deviceLogImport.col.line"),
      enableSorting: false,
      meta: { headerClassName: "w-20 pl-6", cellClassName: "pl-6 tabular-nums" },
      cell: ({ row }) => row.original.line,
    },
    {
      id: "code",
      header: t("attendance:deviceLogImport.col.reason"),
      enableSorting: false,
      cell: ({ row }) => t(`attendance:deviceLogImport.errorCodes.${row.original.code}`, row.original.code),
    },
    {
      id: "value",
      header: t("attendance:deviceLogImport.col.value"),
      enableSorting: false,
      meta: { cellClassName: "pr-6" },
      cell: ({ row }) => row.original.value ?? "—",
    },
  ];

  const table = useDataTable({ columns, data: result?.errors ?? [], getRowId: (row) => String(row.line) });

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Upload size={24} /> {t("attendance:deviceLogImport.title")}
        </Title>
        <Text c="dimmed">{t("attendance:deviceLogImport.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Group justify="space-between" align="center" wrap="wrap" gap="sm">
            <Text size="sm" c="dimmed">
              {t("attendance:deviceLogImport.formatHint")}
            </Text>
            <Button variant="default" size="xs" leftSection={<Download size={14} />} onClick={downloadSample}>
              {t("attendance:deviceLogImport.downloadSample")}
            </Button>
          </Group>
          <FileDropzone
            accept=".csv,text/csv"
            maxBytes={MAX_BYTES}
            busy={mutation.isPending}
            title={t("attendance:deviceLogImport.dropzoneTitle")}
            hint={t("attendance:deviceLogImport.dropzoneHint")}
            busyLabel={t("attendance:deviceLogImport.uploading")}
            onFile={(file) => mutation.mutate(file)}
            onReject={handleReject}
          />
        </Stack>
      </Card>

      {result && (
        <Card withBorder padding="lg">
          <Stack gap="md">
            <SimpleGrid cols={{ base: 2 }} spacing="md">
              <div>
                <Text size="xs" c="dimmed">
                  {t("attendance:deviceLogImport.imported")}
                </Text>
                <Text size="xl" fw={700} c="green">
                  {result.imported}
                </Text>
              </div>
              <div>
                <Text size="xs" c="dimmed">
                  {t("attendance:deviceLogImport.skipped")}
                </Text>
                <Text size="xl" fw={700} c={result.skipped > 0 ? "red" : undefined}>
                  {result.skipped}
                </Text>
              </div>
            </SimpleGrid>

            {result.errors.length > 0 && (
              <>
                <Alert color="yellow" variant="light" icon={<FileWarning size={16} />}>
                  {t("attendance:deviceLogImport.errorsHint")}
                </Alert>
                <DataTable table={table} />
              </>
            )}
          </Stack>
        </Card>
      )}
    </div>
  );
}
