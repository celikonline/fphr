import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { Download, ScrollText, Search, X } from "lucide-react";
import { Badge, Button, Card, Group, Input, Select, Text, TextInput, Title, type BadgeProps } from "@mantine/core";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import PaginationFooter from "@/components/pagination-footer";
import {
  exportAuditEventsToCsv,
  listAuditActionTypes,
  listAuditActors,
  listAuditEvents,
  type AuditActionType,
  type AuditEvent,
} from "@/services/audit.service";

const ALL_VALUE = "__all__";
const DEFAULT_PAGE_SIZE = 20;
const COLUMN_VISIBILITY_KEY = "senseik.audit.grid.columns";
/** The audit service only sorts by timestamp. */
const SORT_FIELD = "occurredAt";

const ACTION_BADGE: Record<AuditActionType, Pick<BadgeProps, "variant" | "color">> = {
  create: { variant: "filled", color: "green" },
  update: { variant: "filled" },
  delete: { variant: "filled", color: "red" },
  login: { variant: "light", color: "gray" },
  invite: { variant: "light", color: "gray" },
  role_change: { variant: "outline", color: "gray" },
};

export default function AuditLogPage() {
  const { t, i18n } = useTranslation(["audit", "common"]);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sortDesc, setSortDesc] = useState(true);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [actor, setActor] = useState(ALL_VALUE);
  const [action, setAction] = useState(ALL_VALUE);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  // Back to the first page whenever the effective filters change (adjusted during render, not in an effect).
  const filterKey = JSON.stringify([debouncedSearch, actor, action, from, to]);
  const [prevFilterKey, setPrevFilterKey] = useState(filterKey);
  if (prevFilterKey !== filterKey) {
    setPrevFilterKey(filterKey);
    setPage(1);
  }

  const sortParam = `${sortDesc ? "-" : ""}${SORT_FIELD}`;

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["audit-events", page, pageSize, sortParam, debouncedSearch, actor, action, from, to],
    queryFn: () =>
      listAuditEvents({
        page,
        pageSize,
        sort: sortParam,
        q: debouncedSearch || undefined,
        actor: actor === ALL_VALUE ? undefined : actor,
        action: action === ALL_VALUE ? undefined : (action as AuditActionType),
        from: from ? new Date(from).toISOString() : undefined,
        to: to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined,
      }),
    placeholderData: (previous) => previous,
  });

  const actors = listAuditActors();
  const actionTypes = listAuditActionTypes();
  const hasFilters = actor !== ALL_VALUE || action !== ALL_VALUE || from !== "" || to !== "" || search !== "";

  function clearFilters() {
    setSearch("");
    setActor(ALL_VALUE);
    setAction(ALL_VALUE);
    setFrom("");
    setTo("");
  }

  function handleExport() {
    if (!data || data.items.length === 0) return;
    const blob = exportAuditEventsToCsv(data.items);
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  function formatTimestamp(value: string) {
    return new Date(value).toLocaleString(i18n.language === "tr" ? "tr-TR" : "en-US");
  }

  function changeSorting(next: SortingState) {
    const [first] = next;
    if (!first) return;
    setSortDesc(first.desc);
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const columns: ColumnDef<AuditEvent>[] = [
    {
      id: SORT_FIELD,
      accessorKey: "occurredAt",
      header: t("audit:timestamp"),
      meta: { label: t("audit:timestamp"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => (
        <Text size="sm" c="dimmed">
          {formatTimestamp(row.original.occurredAt)}
        </Text>
      ),
    },
    {
      id: "actor",
      accessorKey: "actor",
      enableSorting: false,
      header: t("audit:actor"),
      meta: { label: t("audit:actor") },
    },
    {
      id: "action",
      enableSorting: false,
      header: t("audit:action"),
      meta: { label: t("audit:action") },
      cell: ({ row }) => (
        <Badge {...ACTION_BADGE[row.original.action]}>{t(`audit:actionTypes.${row.original.action}`)}</Badge>
      ),
    },
    {
      id: "resource",
      accessorKey: "resource",
      enableSorting: false,
      header: t("audit:resource"),
      meta: { label: t("audit:resource") },
    },
    {
      id: "details",
      enableSorting: false,
      header: t("audit:details"),
      meta: { label: t("audit:details"), cellClassName: "max-w-md" },
      cell: ({ row }) => (
        <Text size="sm" c="dimmed" truncate="end" title={row.original.details}>
          {row.original.details}
        </Text>
      ),
    },
  ];

  const table = useDataTable({
    columns,
    data: data?.items ?? [],
    getRowId: (event) => event.id,
    sorting: [{ id: SORT_FIELD, desc: sortDesc }],
    onSortingChange: changeSorting,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ScrollText className="h-6 w-6" /> {t("audit:title")}
          </Title>
          <Text c="dimmed">{t("audit:subtitle")}</Text>
        </div>
        <Button
          type="button"
          variant="default"
          leftSection={<Download className="h-4 w-4" />}
          onClick={handleExport}
          disabled={!data || data.items.length === 0}
        >
          {t("audit:exportCsv")}
        </Button>
      </Group>

      <Card withBorder padding="md">
        <Group align="flex-end" gap="sm">
          <TextInput
            id="audit-search"
            className="min-w-[14rem] flex-1"
            label={t("common:search")}
            leftSection={<Search className="h-4 w-4" />}
            placeholder={t("audit:searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
          />
          <Select
            className="w-56"
            label={t("audit:actor")}
            value={actor}
            onChange={(value) => setActor(value ?? ALL_VALUE)}
            allowDeselect={false}
            data={[{ value: ALL_VALUE, label: t("audit:allActors") }, ...actors.map((a) => ({ value: a, label: a }))]}
          />
          <Select
            className="w-48"
            label={t("audit:action")}
            value={action}
            onChange={(value) => setAction(value ?? ALL_VALUE)}
            allowDeselect={false}
            data={[
              { value: ALL_VALUE, label: t("audit:allActions") },
              ...actionTypes.map((a) => ({ value: a, label: t(`audit:actionTypes.${a}`) })),
            ]}
          />
          <Input.Wrapper id="audit-from" label={t("audit:from")}>
            <DatePicker id="audit-from" value={from} max={to || undefined} clearable onChange={setFrom} />
          </Input.Wrapper>
          <Input.Wrapper id="audit-to" label={t("audit:to")}>
            <DatePicker id="audit-to" value={to} min={from || undefined} clearable onChange={setTo} />
          </Input.Wrapper>
          {hasFilters && (
            <Button
              type="button"
              variant="subtle"
              size="sm"
              leftSection={<X className="h-4 w-4" />}
              onClick={clearFilters}
            >
              {t("audit:clearFilters")}
            </Button>
          )}
          <DataTableViewOptions table={table} className="ml-auto" />
        </Group>
      </Card>

      <Card withBorder padding={0}>
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("audit:empty")} />
        {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
      </Card>
    </div>
  );
}
