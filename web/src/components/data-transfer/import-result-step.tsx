import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Alert,
  Button,
  Card,
  ColorSwatch,
  Group,
  RingProgress,
  SimpleGrid,
  Stack,
  Tabs,
  Text,
  Title,
  type MantineColor,
} from "@mantine/core";
import { AlertTriangle, CheckCircle2, Download, Upload, XCircle } from "lucide-react";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  DataTransferPermissions,
  IMPORT_ROW_OUTCOMES,
  downloadImportJobResult,
  listImportJobRows,
  saveDownloadedFile,
  type ImportJob,
  type ImportJobRow,
  type ImportRowOutcome,
  type ImportTemplate,
  type RowError,
} from "@/services/data-transfer.service";
import { RowOutcomeBadge } from "./job-badges";
import { useApiLanguage } from "./use-api-language";
import WizardSteps from "./wizard-steps";

const ALL = "all";
const DEFAULT_PAGE_SIZE = 20;

interface ImportResultStepProps {
  job: ImportJob;
  template: ImportTemplate | undefined;
}

function resultFileName(fileName: string, suffix: string): string {
  const base = fileName.replace(/\.[^.]+$/, "") || "import";
  return `${base}-${suffix}.xlsx`;
}

export default function ImportResultStep({ job, template }: ImportResultStepProps) {
  const { t } = useTranslation(["dataTransfer", "common"]);
  const navigate = useNavigate();
  const lang = useApiLanguage();
  const canDownloadResult = usePermission(DataTransferPermissions.ResultDownload);
  const [outcome, setOutcome] = useState<string>(ALL);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  // Rows are server-paged in row-number order (the API has no sort parameter), so columns are not sortable.
  const rowsQuery = useQuery({
    queryKey: ["datatransfer", "import-job-rows", job.id, outcome, page, pageSize],
    queryFn: () =>
      listImportJobRows(job.id, {
        outcome: outcome === ALL ? undefined : (outcome as ImportRowOutcome),
        page,
        pageSize,
      }),
    placeholderData: (previous) => previous,
  });

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const downloadMutation = useMutation({
    mutationFn: () => downloadImportJobResult(job.id, resultFileName(job.fileName, t("dataTransfer:result.fileSuffix")), lang),
    onSuccess: saveDownloadedFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("dataTransfer:result.downloadError")),
      }),
  });

  const columnLabel = (column: string | null) => {
    if (!column) return null;
    return template?.columns.find((c) => c.key === column)?.header ?? column;
  };

  const errorText = (error: RowError) => error.message || t(`dataTransfer:errorCodes.${error.code}`, error.code);

  const columns: ColumnDef<ImportJobRow>[] = [
    {
      id: "rowNumber",
      enableSorting: false,
      header: t("dataTransfer:result.rowNumber"),
      meta: { headerClassName: "w-24 pl-6", cellClassName: "pl-6 tabular-nums" },
      cell: ({ row }) => row.original.rowNumber,
    },
    {
      id: "matchKey",
      enableSorting: false,
      header: t("dataTransfer:result.matchKey"),
      cell: ({ row }) => row.original.matchKey ?? "—",
    },
    {
      id: "outcome",
      enableSorting: false,
      header: t("dataTransfer:result.outcome"),
      cell: ({ row }) => <RowOutcomeBadge outcome={row.original.outcome} />,
    },
    {
      id: "errors",
      enableSorting: false,
      header: t("dataTransfer:result.errorDescription"),
      meta: { headerClassName: "pr-6", cellClassName: "pr-6 whitespace-normal" },
      cell: ({ row }) =>
        row.original.errors.length === 0 ? (
          <Text span c="dimmed" inherit>
            —
          </Text>
        ) : (
          <ul className="space-y-0.5">
            {row.original.errors.map((error, i) => (
              <Text component="li" key={i} size="sm">
                {columnLabel(error.column) && (
                  <Text span fw={500} inherit>
                    {columnLabel(error.column)}:{" "}
                  </Text>
                )}
                {errorText(error)}
              </Text>
            ))}
          </ul>
        ),
    },
  ];

  const table = useDataTable({
    columns,
    data: rowsQuery.data?.items ?? [],
    getRowId: (row) => String(row.rowNumber),
  });

  // Ring shares are relative to the processed rows; the total never falls below their sum.
  const processed = job.insertedCount + job.updatedCount + job.skippedCount + job.errorCount;
  const ringBase = Math.max(job.totalRows, processed, 1);
  const counters: { key: string; value: number; color: MantineColor }[] = [
    { key: "inserted", value: job.insertedCount, color: "green" },
    { key: "updated", value: job.updatedCount, color: "brandBlue" },
    { key: "skipped", value: job.skippedCount, color: "gray" },
    { key: "errors", value: job.errorCount, color: "red" },
  ];
  const ringSections = counters
    .filter((counter) => counter.value > 0)
    .map((counter) => ({
      value: (counter.value / ringBase) * 100,
      color: counter.color,
      tooltip: `${t(`dataTransfer:counts.${counter.key}`)}: ${counter.value}`,
    }));

  return (
    <div className="space-y-6">
      <WizardSteps active={["result"]} />

      {job.status === "Completed" ? (
        <Alert variant="light" color="green" icon={<CheckCircle2 size={16} />} title={t("dataTransfer:result.completed")} />
      ) : job.status === "CompletedWithErrors" ? (
        <Alert
          variant="light"
          color="yellow"
          icon={<AlertTriangle size={16} />}
          title={t("dataTransfer:result.rowsWithErrors", { count: job.errorCount })}
        >
          {t("dataTransfer:result.completedWithErrorsHint")}
        </Alert>
      ) : (
        <Alert variant="light" color="red" icon={<XCircle size={16} />} title={t("dataTransfer:result.failed")}>
          {job.errorCount > 0 && `${t("dataTransfer:result.rowsWithErrors", { count: job.errorCount })} `}
          {job.failurePolicy === "AllOrNothing"
            ? t("dataTransfer:result.failedAllOrNothingHint")
            : t("dataTransfer:result.failedHint")}
        </Alert>
      )}

      <Card withBorder padding="lg">
        <div className="flex flex-col items-center gap-6 sm:flex-row">
          <RingProgress
            size={140}
            thickness={14}
            sectionGap={ringSections.length > 1 ? 2 : 0}
            label={
              <Stack gap={0} align="center">
                <Text size="xl" fw={700} className="tabular-nums">
                  {job.totalRows}
                </Text>
                <Text size="xs" c="dimmed">
                  {t("dataTransfer:counts.total")}
                </Text>
              </Stack>
            }
            sections={ringSections}
          />
          <SimpleGrid cols={{ base: 2, md: 4 }} spacing="md" className="w-full">
            {counters.map((counter) => (
              <Stack key={counter.key} gap={2}>
                <Group gap={6} wrap="nowrap">
                  <ColorSwatch
                    aria-hidden
                    size={10}
                    withShadow={false}
                    color={`var(--mantine-color-${counter.color}-filled)`}
                  />
                  <Text size="xs" c="dimmed">
                    {t(`dataTransfer:counts.${counter.key}`)}
                  </Text>
                </Group>
                <Text
                  size="xl"
                  fw={600}
                  className="tabular-nums"
                  c={counter.key === "errors" && counter.value > 0 ? "red" : undefined}
                >
                  {counter.value}
                </Text>
              </Stack>
            ))}
          </SimpleGrid>
        </div>
      </Card>

      <Group gap="xs">
        {canDownloadResult && (
          <Button
            type="button"
            leftSection={<Download size={16} />}
            loading={downloadMutation.isPending}
            onClick={() => downloadMutation.mutate()}
          >
            {t("dataTransfer:result.download")}
          </Button>
        )}
        <Button
          type="button"
          variant="default"
          leftSection={<Upload size={16} />}
          onClick={() => navigate("/app/settings/data-transfer")}
        >
          {t("dataTransfer:result.newUpload")}
        </Button>
      </Group>

      <Card withBorder padding={0}>
        <Group justify="space-between" gap="sm" p="lg">
          <Title order={4}>{t("dataTransfer:result.rowsTitle")}</Title>
          <Tabs
            variant="pills"
            value={outcome}
            onChange={(value) => {
              setOutcome(value ?? ALL);
              setPage(1);
            }}
          >
            <Tabs.List>
              <Tabs.Tab value={ALL}>{t("dataTransfer:outcomeFilter.all")}</Tabs.Tab>
              {IMPORT_ROW_OUTCOMES.map((value) => (
                <Tabs.Tab key={value} value={value}>
                  {t(`dataTransfer:outcomeFilter.${value}`)}
                </Tabs.Tab>
              ))}
            </Tabs.List>
          </Tabs>
        </Group>
        {rowsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("dataTransfer:result.rowsLoadError")}
          </Text>
        ) : (
          <>
            <DataTable
              table={table}
              isLoading={rowsQuery.isLoading}
              isFetching={rowsQuery.isFetching}
              emptyMessage={t("dataTransfer:result.rowsEmpty")}
            />
            {rowsQuery.data && (
              <PaginationFooter
                result={rowsQuery.data}
                onPageChange={setPage}
                pageSize={pageSize}
                onPageSizeChange={changePageSize}
              />
            )}
          </>
        )}
      </Card>
    </div>
  );
}
