import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent } from "@/components/ui/card";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import PaginationFooter from "@/components/pagination-footer";
import { formatDateTime } from "@/lib/dates";
import { listImportJobs, type ImportJobListItem, type ImportTemplate } from "@/services/data-transfer.service";
import { JobStatusBadge } from "./job-badges";

const DEFAULT_PAGE_SIZE = 20;
const COLUMN_VISIBILITY_KEY = "senseik.import.history.columns";

interface ImportHistoryProps {
  templates: ImportTemplate[] | undefined;
}

const jobPath = (id: string) => `/app/settings/data-transfer/jobs/${id}`;

export default function ImportHistory({ templates }: ImportHistoryProps) {
  const { t, i18n } = useTranslation(["dataTransfer", "common"]);
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  // The API returns jobs newest first and has no sort parameter, so columns are not sortable.
  const { data, isLoading, isFetching, isError } = useQuery({
    queryKey: ["datatransfer", "import-jobs", page, pageSize],
    queryFn: () => listImportJobs({ page, pageSize }),
    placeholderData: (previous) => previous,
  });

  const templateName = (code: string) => templates?.find((tpl) => tpl.code === code)?.name ?? code;

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const countColumn = (
    id: "insertedCount" | "updatedCount" | "skippedCount",
    labelKey: string
  ): ColumnDef<ImportJobListItem> => ({
    id,
    enableSorting: false,
    header: t(labelKey),
    meta: { label: t(labelKey), headerClassName: "text-right", cellClassName: "text-right tabular-nums" },
    cell: ({ row }) => row.original[id],
  });

  const columns: ColumnDef<ImportJobListItem>[] = [
    {
      id: "template",
      enableSorting: false,
      enableHiding: false,
      header: t("dataTransfer:history.template"),
      meta: { label: t("dataTransfer:history.template"), cellClassName: "font-medium" },
      cell: ({ row }) => (
        // A real link keeps the job reachable from the keyboard; the row click covers the mouse.
        <Link to={jobPath(row.original.id)} className="hover:underline" onClick={(e) => e.stopPropagation()}>
          {templateName(row.original.templateCode)}
        </Link>
      ),
    },
    {
      id: "file",
      enableSorting: false,
      header: t("dataTransfer:history.file"),
      meta: { label: t("dataTransfer:history.file"), cellClassName: "max-w-[16rem]" },
      cell: ({ row }) => (
        <span className="block truncate" title={row.original.fileName}>
          {row.original.fileName}
        </span>
      ),
    },
    {
      id: "mode",
      enableSorting: false,
      header: t("dataTransfer:history.mode"),
      meta: { label: t("dataTransfer:history.mode") },
      cell: ({ row }) => t(`dataTransfer:mode.${row.original.mode}`, row.original.mode),
    },
    {
      id: "status",
      enableSorting: false,
      header: t("dataTransfer:history.status"),
      meta: { label: t("dataTransfer:history.status") },
      cell: ({ row }) => <JobStatusBadge status={row.original.status} />,
    },
    countColumn("insertedCount", "dataTransfer:counts.inserted"),
    countColumn("updatedCount", "dataTransfer:counts.updated"),
    countColumn("skippedCount", "dataTransfer:counts.skipped"),
    {
      id: "errorCount",
      enableSorting: false,
      header: t("dataTransfer:counts.errors"),
      meta: { label: t("dataTransfer:counts.errors"), headerClassName: "text-right", cellClassName: "text-right tabular-nums" },
      cell: ({ row }) =>
        row.original.errorCount > 0 ? (
          <span className="text-destructive">{row.original.errorCount}</span>
        ) : (
          row.original.errorCount
        ),
    },
    {
      id: "createdAt",
      enableSorting: false,
      header: t("dataTransfer:history.date"),
      meta: { label: t("dataTransfer:history.date"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateTime(row.original.createdAt, i18n.language),
    },
    {
      id: "requestedBy",
      enableSorting: false,
      header: t("dataTransfer:history.user"),
      meta: { label: t("dataTransfer:history.user") },
      cell: ({ row }) => row.original.requestedByName ?? "—",
    },
  ];

  const table = useDataTable({
    columns,
    data: data?.items ?? [],
    getRowId: (job) => job.id,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
  });

  if (isError) {
    return (
      <Card>
        <CardContent className="p-0">
          <p className="p-6 text-center text-sm text-destructive">{t("dataTransfer:history.loadError")}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <DataTableViewOptions table={table} />
      </div>
      <Card>
        <CardContent className="p-0">
          <DataTable
            table={table}
            isLoading={isLoading}
            isFetching={isFetching}
            emptyMessage={t("dataTransfer:history.empty")}
            onRowClick={(job) => navigate(jobPath(job.id))}
          />
          {data && (
            <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
