import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Download, FileWarning } from "lucide-react";
import { Badge, Button, Card, Group, SegmentedControl, Select, Tabs, Text, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import { formatDateOnly } from "@/lib/dates";
import {
  exportMissingDocumentReportToCsv,
  getExpiringDocuments,
  getMissingDocumentReport,
  type DocumentExpiryState,
  type DocumentRequirementStatus,
  type ExpiringDocumentItem,
  type MissingDocumentReportItem,
} from "@/services/documents.service";

const ALL_VALUE = "__all__";
const DEFAULT_PAGE_SIZE = 20;
const STATUSES: DocumentRequirementStatus[] = ["missing", "uploaded", "waived"];

const STATUS_COLOR: Record<DocumentRequirementStatus, string> = {
  missing: "red",
  uploaded: "green",
  waived: "gray",
};

export default function MissingDocumentsReportPage() {
  const canView = usePermission("documents.requirements.view");
  if (!canView) return <NoAccess />;
  return <MissingDocumentsReport />;
}

function MissingDocumentsReport() {
  const { t } = useTranslation(["documents", "common"]);

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <FileWarning size={24} /> {t("documents:report.title")}
        </Title>
        <Text c="dimmed">{t("documents:report.subtitle")}</Text>
      </div>

      <Tabs defaultValue="missing">
        <Tabs.List>
          <Tabs.Tab value="missing">{t("documents:report.tabs.missing")}</Tabs.Tab>
          <Tabs.Tab value="expiring">{t("documents:report.tabs.expiring")}</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="missing" pt="md">
          <MissingDocumentsPanel />
        </Tabs.Panel>
        <Tabs.Panel value="expiring" pt="md">
          <ExpiringDocumentsPanel />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}

function MissingDocumentsPanel() {
  const { t, i18n } = useTranslation(["documents", "common"]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [status, setStatus] = useState<DocumentRequirementStatus | typeof ALL_VALUE>("missing");

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["documents", "missing-report", page, pageSize, status],
    queryFn: () => getMissingDocumentReport({ page, pageSize, status: status === ALL_VALUE ? undefined : status }),
    placeholderData: (previous) => previous,
  });

  function changeStatus(value: string | null) {
    setStatus((value as DocumentRequirementStatus | null) ?? ALL_VALUE);
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  function handleExport() {
    if (!data || data.items.length === 0) return;
    const blob = exportMissingDocumentReportToCsv(data.items);
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `missing-documents-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  const columns: ColumnDef<MissingDocumentReportItem>[] = [
    {
      id: "employeeName",
      accessorKey: "employeeName",
      header: t("documents:report.columns.employee"),
      meta: { label: t("documents:report.columns.employee") },
      cell: ({ row }) => (
        <Text component={Link} to={`/app/employees/${row.original.employeeId}/documents`} size="sm" fw={500} className="hover:underline">
          {row.original.employeeName}
        </Text>
      ),
    },
    {
      id: "documentTypeName",
      accessorKey: "documentTypeName",
      header: t("documents:report.columns.documentType"),
      meta: { label: t("documents:report.columns.documentType") },
    },
    {
      id: "stage",
      accessorKey: "stage",
      header: t("documents:report.columns.stage"),
      meta: { label: t("documents:report.columns.stage") },
      cell: ({ row }) => t(`documents:enum.stage.${row.original.stage}`),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("documents:report.columns.status"),
      meta: { label: t("documents:report.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant="filled" color={STATUS_COLOR[row.original.status]}>
          {t(`documents:enum.status.${row.original.status}`)}
        </Badge>
      ),
    },
    {
      id: "dueDate",
      accessorKey: "dueDate",
      header: t("documents:report.columns.dueDate"),
      meta: { label: t("documents:report.columns.dueDate"), headerClassName: "w-32" },
      cell: ({ row }) => date(row.original.dueDate),
    },
  ];

  const table = useDataTable({ columns, data: data?.items ?? [], getRowId: (r) => r.requirementId });

  return (
    <div className="space-y-4">
      <Group justify="flex-end">
        <Button
          type="button"
          variant="default"
          leftSection={<Download size={16} />}
          onClick={handleExport}
          disabled={!data || data.items.length === 0}
        >
          {t("documents:report.exportCsv")}
        </Button>
      </Group>

      <Card withBorder padding="md">
        <Group align="flex-end" gap="sm">
          <Select
            w={220}
            label={t("documents:report.columns.status")}
            data={[
              { value: ALL_VALUE, label: t("documents:report.allStatuses") },
              ...STATUSES.map((option) => ({ value: option, label: t(`documents:enum.status.${option}`) })),
            ]}
            value={status}
            onChange={changeStatus}
            allowDeselect={false}
          />
        </Group>
      </Card>

      <Card withBorder padding={0}>
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("documents:report.empty")} />
        {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
      </Card>
    </div>
  );
}

const EXPIRY_STATE_COLOR: Record<DocumentExpiryState, string> = {
  expiring: "orange",
  expired: "red",
};

/** New tab (S-6 sözleşmesi, Faz 4 tur 3): a document's own validity date, distinct from the requirement due date. */
function ExpiringDocumentsPanel() {
  const { t, i18n } = useTranslation(["documents", "common"]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [expiryState, setExpiryState] = useState<DocumentExpiryState>("expiring");

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["documents", "expiring-report", page, pageSize, expiryState],
    queryFn: () => getExpiringDocuments({ page, pageSize, withinDays: 30, state: expiryState }),
    placeholderData: (previous) => previous,
  });

  function changeExpiryState(value: string) {
    setExpiryState(value as DocumentExpiryState);
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const date = (value: string) => formatDateOnly(value, i18n.language);

  const columns: ColumnDef<ExpiringDocumentItem>[] = [
    {
      id: "employeeName",
      accessorKey: "employeeName",
      header: t("documents:report.columns.employee"),
      meta: { label: t("documents:report.columns.employee") },
      cell: ({ row }) => (
        <Text component={Link} to={`/app/employees/${row.original.employeeId}/documents`} size="sm" fw={500} className="hover:underline">
          {row.original.employeeName}
        </Text>
      ),
    },
    {
      id: "documentTypeName",
      accessorKey: "documentTypeName",
      header: t("documents:report.columns.documentType"),
      meta: { label: t("documents:report.columns.documentType") },
    },
    {
      id: "expiryDate",
      accessorKey: "expiryDate",
      header: t("documents:report.columns.expiryDate"),
      meta: { label: t("documents:report.columns.expiryDate"), headerClassName: "w-32" },
      cell: ({ row }) => date(row.original.expiryDate),
    },
    {
      id: "daysLeft",
      accessorKey: "daysLeft",
      header: t("documents:report.columns.daysLeft"),
      meta: { label: t("documents:report.columns.daysLeft"), headerClassName: "w-40" },
      cell: ({ row }) => (
        <Badge variant="filled" color={EXPIRY_STATE_COLOR[expiryState]}>
          {row.original.daysLeft < 0
            ? t("documents:report.daysOverdue", { count: Math.abs(row.original.daysLeft) })
            : t("documents:report.daysRemaining", { count: row.original.daysLeft })}
        </Badge>
      ),
    },
  ];

  const table = useDataTable({ columns, data: data?.items ?? [], getRowId: (r) => r.documentId });

  return (
    <div className="space-y-4">
      <Card withBorder padding="md">
        <SegmentedControl
          value={expiryState}
          onChange={changeExpiryState}
          data={[
            { value: "expiring", label: t("documents:report.expiryState.expiring") },
            { value: "expired", label: t("documents:report.expiryState.expired") },
          ]}
        />
      </Card>

      <Card withBorder padding={0}>
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("documents:report.emptyExpiring")} />
        {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
      </Card>
    </div>
  );
}
