import { useRef, useState, type ChangeEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { FileUp, Upload } from "lucide-react";
import { Badge, Button, Card, Group, Stack, Table, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  importPayslipFiles,
  type PayslipImportFileResult,
  type PayslipImportFileStatus,
  type PayslipImportResult,
} from "@/services/payslip-imports.service";

const ACCEPT = ".pdf,.zip,application/pdf,application/zip,application/x-zip-compressed";

const STATUS_COLOR: Record<PayslipImportFileStatus, string> = {
  imported: "teal",
  unmatched: "orange",
  duplicate: "yellow",
  terminated: "grape",
  skipped: "gray",
  failed: "red",
};

/**
 * E3: dış bordro PDF'lerini (tek tek ya da zip) `<sicil>_<YYYY-MM>.pdf` adına göre çalışan belgelerine dağıtır
 * (`POST documents/payslip-imports`, payroll.payslip.import). Eşleşmeyen dosyalar hata değil, sonuç raporunda listelenir.
 */
export default function PayslipImportPage() {
  const canImport = usePermission("payroll.payslip.import");
  if (!canImport) return <NoAccess />;
  return <PayslipImportScreen />;
}

function PayslipImportScreen() {
  const { t } = useTranslation(["payroll", "common"]);
  const inputRef = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<File[]>([]);
  const [result, setResult] = useState<PayslipImportResult | null>(null);

  const mutation = useMutation({
    mutationFn: (selected: File[]) => importPayslipFiles(selected),
    onSuccess: (data) => {
      setResult(data);
      setFiles([]);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("payroll:payslipImport.uploadFailed")) }),
  });

  function filesSelected(event: ChangeEvent<HTMLInputElement>) {
    setFiles(Array.from(event.target.files ?? []));
    event.target.value = "";
  }

  function statusLabel(file: PayslipImportFileResult): string {
    if (file.status === "skipped" && file.reason === "not_pdf") return t("payroll:payslipImport.status.notPdf");
    return t(`payroll:payslipImport.status.${file.status}`);
  }

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <FileUp size={24} /> {t("payroll:payslipImport.title")}
        </Title>
        <Text c="dimmed">{t("payroll:payslipImport.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("payroll:payslipImport.formatHint")}
          </Text>
          <input ref={inputRef} data-testid="payslip-files" type="file" multiple accept={ACCEPT} className="hidden" onChange={filesSelected} />
          <Group gap="sm">
            <Button type="button" variant="default" leftSection={<Upload size={16} />} onClick={() => inputRef.current?.click()}>
              {t("payroll:payslipImport.selectFiles")}
            </Button>
            {files.length > 0 && <Text size="sm">{t("payroll:payslipImport.selected", { count: files.length })}</Text>}
            <Button type="button" disabled={files.length === 0} loading={mutation.isPending} onClick={() => mutation.mutate(files)}>
              {t("payroll:payslipImport.upload")}
            </Button>
          </Group>
        </Stack>
      </Card>

      {result && (
        <Card withBorder padding="lg">
          <Stack gap="md">
            <Text fw={600}>{t("payroll:payslipImport.summary", { imported: result.imported, total: result.total })}</Text>
            <Table.ScrollContainer minWidth={560}>
              <Table striped withRowBorders>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("payroll:payslipImport.columns.file")}</Table.Th>
                    <Table.Th>{t("payroll:payslipImport.columns.status")}</Table.Th>
                    <Table.Th>{t("payroll:payslipImport.columns.period")}</Table.Th>
                    <Table.Th>{t("payroll:payslipImport.columns.reason")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {result.files.map((file, index) => (
                    <Table.Tr key={`${file.fileName}-${index}`}>
                      <Table.Td>{file.fileName}</Table.Td>
                      <Table.Td>
                        <Badge variant="light" color={STATUS_COLOR[file.status]} size="sm">
                          {statusLabel(file)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>{file.period ?? "—"}</Table.Td>
                      <Table.Td>
                        {file.reason && file.reason !== "not_pdf" ? t(`payroll:payslipImport.reasons.${file.reason}`, file.reason) : "—"}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          </Stack>
        </Card>
      )}
    </div>
  );
}
