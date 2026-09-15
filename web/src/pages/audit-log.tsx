import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { Download, ScrollText, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
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

const ACTION_BADGE: Record<AuditActionType, "default" | "secondary" | "destructive" | "outline" | "success"> = {
  create: "success",
  update: "default",
  delete: "destructive",
  login: "secondary",
  invite: "secondary",
  role_change: "outline",
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
      meta: { label: t("audit:timestamp"), cellClassName: "whitespace-nowrap text-muted-foreground" },
      cell: ({ row }) => formatTimestamp(row.original.occurredAt),
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
        <Badge variant={ACTION_BADGE[row.original.action]}>{t(`audit:actionTypes.${row.original.action}`)}</Badge>
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
      meta: { label: t("audit:details"), cellClassName: "max-w-md text-muted-foreground" },
      cell: ({ row }) => (
        <div className="truncate" title={row.original.details}>
          {row.original.details}
        </div>
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
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <ScrollText className="h-6 w-6" /> {t("audit:title")}
          </h1>
          <p className="text-muted-foreground">{t("audit:subtitle")}</p>
        </div>
        <Button type="button" variant="outline" onClick={handleExport} disabled={!data || data.items.length === 0}>
          <Download className="mr-2 h-4 w-4" /> {t("audit:exportCsv")}
        </Button>
      </div>

      <Card>
        <CardContent className="flex flex-wrap items-end gap-3 p-4">
          <div className="min-w-[14rem] flex-1">
            <Label htmlFor="audit-search">{t("common:search")}</Label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="audit-search"
                className="pl-9"
                placeholder={t("audit:searchPlaceholder")}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>
          <div>
            <Label>{t("audit:actor")}</Label>
            <Select value={actor} onValueChange={setActor}>
              <SelectTrigger className="w-56">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_VALUE}>{t("audit:allActors")}</SelectItem>
                {actors.map((a) => (
                  <SelectItem key={a} value={a}>
                    {a}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label>{t("audit:action")}</Label>
            <Select value={action} onValueChange={setAction}>
              <SelectTrigger className="w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_VALUE}>{t("audit:allActions")}</SelectItem>
                {actionTypes.map((a) => (
                  <SelectItem key={a} value={a}>
                    {t(`audit:actionTypes.${a}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label htmlFor="audit-from">{t("audit:from")}</Label>
            <DatePicker id="audit-from" value={from} max={to || undefined} clearable onChange={setFrom} />
          </div>
          <div>
            <Label htmlFor="audit-to">{t("audit:to")}</Label>
            <DatePicker id="audit-to" value={to} min={from || undefined} clearable onChange={setTo} />
          </div>
          {hasFilters && (
            <Button type="button" variant="ghost" size="sm" onClick={clearFilters}>
              <X className="mr-1 h-4 w-4" /> {t("audit:clearFilters")}
            </Button>
          )}
          <DataTableViewOptions table={table} className="ml-auto" />
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-0">
          <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("audit:empty")} />
          {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
        </CardContent>
      </Card>
    </div>
  );
}
