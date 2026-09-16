import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { useMediaQuery } from "@mantine/hooks";
import { Download, ScrollText } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Group,
  Input,
  Select,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
  type BadgeProps,
} from "@mantine/core";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { FilterBar } from "@/components/ui/filter-bar";
import PaginationFooter from "@/components/pagination-footer";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import {
  exportAuditEventsCsv,
  listAuditActionTypes,
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
  const canViewSensitive = usePermission("identity.audit.sensitive_view");
  // Narrow phones (Faz 1 tur 1 FE md.3): a card list reads better than a horizontally-scrolled table.
  const isMobile = useMediaQuery("(max-width: 390px)");

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sortDesc, setSortDesc] = useState(true);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  // Backend contract (S-1) has no actors-lookup endpoint - `actor` is a free-text filter, debounced
  // like search, rather than a Select sourced from a static/mock list.
  const [actor, setActor] = useState("");
  const [debouncedActor, setDebouncedActor] = useState("");
  const [action, setAction] = useState(ALL_VALUE);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedActor(actor), 300);
    return () => clearTimeout(timer);
  }, [actor]);

  // Back to the first page whenever the effective filters change (adjusted during render, not in an effect).
  const filterKey = JSON.stringify([debouncedSearch, debouncedActor, action, from, to]);
  const [prevFilterKey, setPrevFilterKey] = useState(filterKey);
  if (prevFilterKey !== filterKey) {
    setPrevFilterKey(filterKey);
    setPage(1);
  }

  const sortParam = `${sortDesc ? "-" : ""}${SORT_FIELD}`;

  const activeFilters = {
    actor: debouncedActor || undefined,
    action: action === ALL_VALUE ? undefined : (action as AuditActionType),
    from: from ? new Date(from).toISOString() : undefined,
    to: to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined,
  };

  const { data, isLoading, isFetching, error } = useQuery({
    queryKey: ["audit-events", page, pageSize, sortParam, debouncedSearch, activeFilters],
    queryFn: () =>
      listAuditEvents({
        page,
        pageSize,
        sort: sortParam,
        q: debouncedSearch || undefined,
        ...activeFilters,
      }),
    placeholderData: (previous) => previous,
    // 403 (missing `identity.audit.view`) is expected for some roles - don't retry it, show <NoAccess>.
    retry: (count, err) => getApiErrorStatus(err) !== 403 && count < 1,
  });

  const actionTypes = listAuditActionTypes();
  const hasFilters = actor !== "" || action !== ALL_VALUE || from !== "" || to !== "" || search !== "";

  function clearFilters() {
    setSearch("");
    setActor("");
    setAction(ALL_VALUE);
    setFrom("");
    setTo("");
  }

  const exportMutation = useMutation({
    mutationFn: () => exportAuditEventsCsv(activeFilters),
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("common:errorGeneric")),
      }),
  });

  function handleExport() {
    if (!data || data.totalCount === 0) return;
    exportMutation.mutate();
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
      cell: ({ row }) => <AuditDetailsCell event={row.original} canViewSensitive={canViewSensitive} t={t} />,
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

  if (getApiErrorStatus(error) === 403) return <NoAccess />;

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
          loading={exportMutation.isPending}
          disabled={!data || data.totalCount === 0}
        >
          {t("audit:exportCsv")}
        </Button>
      </Group>

      <FilterBar
        search={{ id: "audit-search", value: search, onChange: setSearch, placeholder: t("audit:searchPlaceholder") }}
        hasActiveFilters={hasFilters}
        onClearFilters={clearFilters}
        trailing={<DataTableViewOptions table={table} className="ml-auto" />}
      >
        <TextInput
          id="audit-actor"
          className="w-56"
          label={t("audit:actor")}
          placeholder={t("audit:actorPlaceholder")}
          value={actor}
          onChange={(e) => setActor(e.currentTarget.value)}
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
      </FilterBar>

      {isMobile ? (
        <AuditLogMobileList
          items={data?.items ?? []}
          isLoading={isLoading}
          canViewSensitive={canViewSensitive}
          formatTimestamp={formatTimestamp}
        />
      ) : (
        <Card withBorder padding={0}>
          <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("audit:empty")} />
        </Card>
      )}
      {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
    </div>
  );
}

/** Details cell: masked placeholder + "no permission" tooltip when the caller lacks `identity.audit.sensitive_view`. */
function AuditDetailsCell({
  event,
  canViewSensitive,
  t,
}: {
  event: AuditEvent;
  canViewSensitive: boolean;
  t: (key: string) => string;
}) {
  if (!canViewSensitive) {
    return (
      <Tooltip label={t("audit:sensitiveHidden")}>
        <Text size="sm" c="dimmed" truncate="end">
          {t("audit:sensitiveMasked")}
        </Text>
      </Tooltip>
    );
  }

  return (
    <Text size="sm" c="dimmed" truncate="end" title={event.details}>
      {event.details}
    </Text>
  );
}

/** ≤390px card list mirroring the desktop table's columns (Faz 1 tur 1 FE md.3). */
function AuditLogMobileList({
  items,
  isLoading,
  canViewSensitive,
  formatTimestamp,
}: {
  items: AuditEvent[];
  isLoading: boolean;
  canViewSensitive: boolean;
  formatTimestamp: (value: string) => string;
}) {
  const { t } = useTranslation(["audit", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (items.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("audit:empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {items.map((event) => (
        <Card key={event.id} withBorder padding="md">
          <Stack gap={4}>
            <Group justify="space-between" align="center" wrap="wrap">
              <Text size="xs" c="dimmed">
                {formatTimestamp(event.occurredAt)}
              </Text>
              <Badge {...ACTION_BADGE[event.action]}>{t(`audit:actionTypes.${event.action}`)}</Badge>
            </Group>
            <Text size="sm" fw={500}>
              {event.actor}
            </Text>
            <Text size="sm" c="dimmed">
              {event.resource}
            </Text>
            <AuditDetailsCell event={event} canViewSensitive={canViewSensitive} t={t} />
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}
