import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { GitBranch, Plus, Search, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks";
import { formatDateOnly } from "@/lib/dates";
import { EMPLOYEE_STATUSES, listEmployees, type EmployeeListItem, type EmployeeSortField } from "@/services/employees.service";
import type { EmployeeFilter, EmployeeSelection } from "@/types";
import { AccountStatusBadge, EmployeeStatusBadge } from "@/components/employees/employee-badges";
import BulkActionsMenu from "@/components/employees/bulk-actions-menu";
import { BULK_ACTIONS } from "@/components/employees/bulk-actions";
import CreateEmployeeDialog from "@/components/employees/create-employee-dialog";
import EmployeeDirectory from "@/components/employees/employee-directory";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";

const DEFAULT_PAGE_SIZE = 20;
const COLUMN_VISIBILITY_KEY = "senseik.employees.grid.columns";
const ALL = "__all__";
const BULK_PERMISSIONS = [...new Set(BULK_ACTIONS.map((a) => a.permission))];

type SelectionState = { mode: "ids"; ids: Set<string> } | { mode: "all"; excluded: Set<string> };

const EMPTY_SELECTION: SelectionState = { mode: "ids", ids: new Set() };

export default function EmployeeListPage() {
  const canRead = usePermission("employee.employees.read");
  const canViewDirectory = usePermission("employee.directory.view");

  if (canRead) return <EmployeeGrid />;
  if (canViewDirectory) return <EmployeeDirectory />;
  return <NoAccess />;
}

function EmployeeGrid() {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canCreate = usePermission("employee.employees.create");
  const canBulk = useAnyPermission(BULK_PERMISSIONS);
  const hasOrgChartPermission = usePermission("employee.orgchart.view");
  const orgChartEnabled = useAccessSetting("showOrgChart");
  const { nameById: unitNames } = useOrgUnits();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [includeSubUnits, setIncludeSubUnits] = useState(true);
  const [status, setStatus] = useState(ALL);
  const [account, setAccount] = useState(ALL);
  const [sort, setSort] = useState<{ field: EmployeeSortField; desc: boolean }>({ field: "displayName", desc: false });
  const [selection, setSelection] = useState<SelectionState>(EMPTY_SELECTION);
  const [createOpen, setCreateOpen] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => clearTimeout(timer);
  }, [search]);

  const filter = useMemo<EmployeeFilter>(
    () => ({
      q: debouncedSearch || null,
      orgUnitId,
      includeSubUnits: orgUnitId ? includeSubUnits : false,
      status: status === ALL ? null : status,
      hasUserAccount: account === ALL ? null : account === "yes",
      managerEmployeeId: null,
    }),
    [debouncedSearch, orgUnitId, includeSubUnits, status, account]
  );

  // A new filter starts from page 1 with an empty selection (adjusted during render, not in an effect).
  const [prevFilter, setPrevFilter] = useState(filter);
  if (prevFilter !== filter) {
    setPrevFilter(filter);
    setPage(1);
    setSelection(EMPTY_SELECTION);
  }

  const sortParam = `${sort.desc ? "-" : ""}${sort.field}`;

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["employees", "list", filter, page, pageSize, sortParam],
    queryFn: () =>
      listEmployees({
        page,
        pageSize,
        sort: sortParam,
        q: filter.q ?? undefined,
        orgUnitId: filter.orgUnitId,
        includeSubUnits: filter.includeSubUnits,
        status: filter.status,
        hasUserAccount: filter.hasUserAccount,
      }),
    placeholderData: (previous) => previous,
  });

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  // Names of every employee seen so far (across pages), so bulk results can label rows from earlier pages.
  const [nameById, setNameById] = useState<ReadonlyMap<string, string>>(() => new Map());
  const [namesSource, setNamesSource] = useState<typeof data>(undefined);
  if (namesSource !== data) {
    setNamesSource(data);
    if (data && data.items.length > 0) {
      setNameById((prev) => {
        const next = new Map(prev);
        for (const item of data.items) next.set(item.id, item.displayName);
        return next;
      });
    }
  }

  const isSelected = (id: string) => (selection.mode === "ids" ? selection.ids.has(id) : !selection.excluded.has(id));
  const selectedOnPage = items.filter((item) => isSelected(item.id)).length;
  const pageFullySelected = items.length > 0 && selectedOnPage === items.length;
  const selectedCount = selection.mode === "ids" ? selection.ids.size : Math.max(0, totalCount - selection.excluded.size);

  const apiSelection: EmployeeSelection | null =
    selectedCount === 0
      ? null
      : selection.mode === "ids"
        ? { employeeIds: [...selection.ids], filter: null, excludedEmployeeIds: null }
        : { employeeIds: null, filter, excludedEmployeeIds: [...selection.excluded] };

  function toggleRow(id: string, checked: boolean) {
    setSelection((prev) => {
      if (prev.mode === "ids") {
        const ids = new Set(prev.ids);
        if (checked) ids.add(id);
        else ids.delete(id);
        return { mode: "ids", ids };
      }
      const excluded = new Set(prev.excluded);
      if (checked) excluded.delete(id);
      else excluded.add(id);
      return { mode: "all", excluded };
    });
  }

  function togglePage(checked: boolean) {
    setSelection((prev) => {
      if (prev.mode === "ids") {
        const ids = new Set(prev.ids);
        items.forEach((item) => (checked ? ids.add(item.id) : ids.delete(item.id)));
        return { mode: "ids", ids };
      }
      const excluded = new Set(prev.excluded);
      items.forEach((item) => (checked ? excluded.delete(item.id) : excluded.add(item.id)));
      return { mode: "all", excluded };
    });
  }

  function changeSorting(next: SortingState) {
    const [first] = next;
    if (!first) return;
    setSort({ field: first.id as EmployeeSortField, desc: first.desc });
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const locale = i18n.language;

  const columns: ColumnDef<EmployeeListItem>[] = [
    ...(canBulk
      ? [
          {
            id: "select",
            enableSorting: false,
            enableHiding: false,
            meta: { headerClassName: "w-10" },
            header: () => (
              <Checkbox
                aria-label={t("employees:bulk.selectPage")}
                checked={pageFullySelected ? true : selectedOnPage > 0 ? "indeterminate" : false}
                onCheckedChange={(checked) => togglePage(checked === true)}
              />
            ),
            cell: ({ row }) => (
              <div onClick={(e) => e.stopPropagation()}>
                <Checkbox
                  aria-label={row.original.displayName}
                  checked={isSelected(row.original.id)}
                  onCheckedChange={(checked) => toggleRow(row.original.id, checked === true)}
                />
              </div>
            ),
          } satisfies ColumnDef<EmployeeListItem>,
        ]
      : []),
    {
      id: "displayName",
      accessorKey: "displayName",
      enableHiding: false,
      header: t("employees:columns.name"),
      meta: { label: t("employees:columns.name"), cellClassName: "font-medium" },
    },
    {
      id: "orgUnit",
      enableSorting: false,
      header: t("employees:columns.orgUnit"),
      meta: { label: t("employees:columns.orgUnit") },
      cell: ({ row }) => (row.original.orgUnitId && unitNames.get(row.original.orgUnitId)) || "—",
    },
    {
      id: "manager",
      enableSorting: false,
      header: t("employees:columns.manager"),
      meta: { label: t("employees:columns.manager") },
      cell: ({ row }) => row.original.managerDisplayName ?? "—",
    },
    {
      id: "workEmail",
      enableSorting: false,
      header: t("employees:columns.workEmail"),
      meta: { label: t("employees:columns.workEmail") },
      cell: ({ row }) => row.original.workEmail ?? "—",
    },
    {
      id: "workPhone",
      enableSorting: false,
      header: t("employees:columns.workPhone"),
      meta: { label: t("employees:columns.workPhone"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => row.original.workPhone ?? "—",
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("employees:columns.status"),
      meta: { label: t("employees:columns.status") },
      cell: ({ row }) => <EmployeeStatusBadge status={row.original.status} />,
    },
    {
      id: "accountStatus",
      enableSorting: false,
      header: t("employees:columns.accountStatus"),
      meta: { label: t("employees:columns.accountStatus") },
      cell: ({ row }) => <AccountStatusBadge status={row.original.accountStatus} />,
    },
    {
      id: "hireDate",
      accessorKey: "hireDate",
      header: t("employees:columns.hireDate"),
      meta: { label: t("employees:columns.hireDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => (row.original.hireDate ? formatDateOnly(row.original.hireDate, locale) : "—"),
    },
  ];

  const table = useDataTable({
    columns,
    data: items,
    getRowId: (employee) => employee.id,
    sorting: [{ id: sort.field, desc: sort.desc }],
    onSortingChange: changeSorting,
    initialColumnVisibility: { workPhone: false },
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
  });

  function onBulkCompleted() {
    setSelection(EMPTY_SELECTION);
    queryClient.invalidateQueries({ queryKey: ["employees"] });
  }

  const hasActiveFilters = filter.q || filter.orgUnitId || filter.status || filter.hasUserAccount !== null;

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Users className="h-6 w-6" /> {t("employees:title")}
          </h1>
          <p className="text-muted-foreground">{t("employees:subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {hasOrgChartPermission && orgChartEnabled && (
            <Button asChild variant="outline">
              <Link to="/app/employees/org-chart">
                <GitBranch className="mr-2 h-4 w-4" /> {t("employees:orgChart.title")}
              </Link>
            </Button>
          )}
          {canCreate && (
            <Button type="button" onClick={() => setCreateOpen(true)}>
              <Plus className="mr-2 h-4 w-4" /> {t("employees:create.button")}
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder={t("employees:searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="w-56">
          <Label htmlFor="emp-filter-unit" className="text-xs text-muted-foreground">
            {t("employees:filters.orgUnit")}
          </Label>
          <OrgUnitSelect id="emp-filter-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
        </div>
        {orgUnitId && (
          <div className="flex h-10 items-center gap-2">
            <Switch id="emp-filter-sub" checked={includeSubUnits} onCheckedChange={setIncludeSubUnits} />
            <Label htmlFor="emp-filter-sub" className="text-sm">
              {t("employees:filters.includeSubUnits")}
            </Label>
          </div>
        )}
        <div className="w-40">
          <Label htmlFor="emp-filter-status" className="text-xs text-muted-foreground">
            {t("employees:filters.status")}
          </Label>
          <Select value={status} onValueChange={setStatus}>
            <SelectTrigger id="emp-filter-status">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>{t("employees:filters.all")}</SelectItem>
              {EMPLOYEE_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {t(`employees:status.${s}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="w-44">
          <Label htmlFor="emp-filter-account" className="text-xs text-muted-foreground">
            {t("employees:filters.hasUserAccount")}
          </Label>
          <Select value={account} onValueChange={setAccount}>
            <SelectTrigger id="emp-filter-account">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>{t("employees:filters.all")}</SelectItem>
              <SelectItem value="yes">{t("employees:filters.withAccount")}</SelectItem>
              <SelectItem value="no">{t("employees:filters.withoutAccount")}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <DataTableViewOptions table={table} className="ml-auto" />
      </div>

      {canBulk && (
        <div className="flex flex-wrap items-center gap-3">
          <BulkActionsMenu
            selection={apiSelection}
            selectedCount={selectedCount}
            nameById={nameById}
            onCompleted={onBulkCompleted}
          />
          <span className="text-sm text-muted-foreground">
            {selectedCount > 0 ? t("employees:bulk.selectedCount", { count: selectedCount }) : t("employees:bulk.selectHint")}
          </span>
          {selectedCount > 0 && (
            <Button type="button" variant="link" size="sm" className="px-0" onClick={() => setSelection(EMPTY_SELECTION)}>
              {t("employees:bulk.clearSelection")}
            </Button>
          )}
        </div>
      )}

      {canBulk && selection.mode === "ids" && pageFullySelected && totalCount > selection.ids.size && (
        <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted/50 px-4 py-2 text-sm">
          <span>{t("employees:bulk.pageSelected", { count: selection.ids.size })}</span>
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto px-0"
            onClick={() => setSelection({ mode: "all", excluded: new Set() })}
          >
            {t("employees:bulk.selectAllMatching", { count: totalCount })}
          </Button>
        </div>
      )}
      {canBulk && selection.mode === "all" && (
        <div className="flex flex-wrap items-center gap-2 rounded-md border border-primary/40 bg-primary/5 px-4 py-2 text-sm">
          <span>
            {t("employees:bulk.allMatchingSelected", { count: selectedCount })}
            {selection.excluded.size > 0 && ` ${t("employees:bulk.excludedCount", { count: selection.excluded.size })}`}
          </span>
        </div>
      )}

      <Card>
        <CardContent className="p-0">
          <DataTable
            table={table}
            isLoading={isLoading}
            isFetching={isFetching}
            stickyHeader
            emptyMessage={hasActiveFilters ? t("employees:emptyFiltered") : t("employees:empty")}
            isRowSelected={(employee) => isSelected(employee.id)}
            onRowClick={(employee) => navigate(`/app/employees/${employee.id}`)}
          />
          {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
        </CardContent>
      </Card>

      <CreateEmployeeDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  );
}
