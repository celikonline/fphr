import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, ArrowUpDown, GitBranch, Plus, Search, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks";
import { formatDateOnly } from "@/lib/dates";
import { EMPLOYEE_STATUSES, listEmployees, type EmployeeSortField } from "@/services/employees.service";
import type { EmployeeFilter, EmployeeSelection } from "@/types";
import { AccountStatusBadge, EmployeeStatusBadge } from "@/components/employees/employee-badges";
import BulkActionsMenu from "@/components/employees/bulk-actions-menu";
import { BULK_ACTIONS } from "@/components/employees/bulk-actions";
import CreateEmployeeDialog from "@/components/employees/create-employee-dialog";
import EmployeeDirectory from "@/components/employees/employee-directory";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";

const PAGE_SIZE = 20;
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
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [includeSubUnits, setIncludeSubUnits] = useState(true);
  const [status, setStatus] = useState(ALL);
  const [account, setAccount] = useState(ALL);
  const [sort, setSort] = useState<{ field: EmployeeSortField; desc: boolean }>({ field: "displayName", desc: false });
  const [selection, setSelection] = useState<SelectionState>(EMPTY_SELECTION);
  const [createOpen, setCreateOpen] = useState(false);
  const namesRef = useRef(new Map<string, string>());

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

  useEffect(() => {
    setPage(1);
    setSelection(EMPTY_SELECTION);
  }, [filter]);

  const sortParam = `${sort.desc ? "-" : ""}${sort.field}`;

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["employees", "list", filter, page, sortParam],
    queryFn: () =>
      listEmployees({
        page,
        pageSize: PAGE_SIZE,
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

  useEffect(() => {
    for (const item of data?.items ?? []) namesRef.current.set(item.id, item.displayName);
  }, [data]);

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

  function toggleSort(field: EmployeeSortField) {
    setSort((prev) => (prev.field === field ? { field, desc: !prev.desc } : { field, desc: false }));
    setPage(1);
  }

  function onBulkCompleted() {
    setSelection(EMPTY_SELECTION);
    queryClient.invalidateQueries({ queryKey: ["employees"] });
  }

  const hasActiveFilters = filter.q || filter.orgUnitId || filter.status || filter.hasUserAccount !== null;
  const locale = i18n.language;

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
      </div>

      {canBulk && (
        <div className="flex flex-wrap items-center gap-3">
          <BulkActionsMenu
            selection={apiSelection}
            selectedCount={selectedCount}
            nameById={namesRef.current}
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
          {isLoading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 5 }, (_, i) => (
                <Skeleton key={i} className="h-8 w-full" />
              ))}
            </div>
          ) : items.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">
              {hasActiveFilters ? t("employees:emptyFiltered") : t("employees:empty")}
            </p>
          ) : (
            <>
              <div className={`overflow-x-auto ${isFetching ? "opacity-70" : ""}`}>
                <Table>
                  <TableHeader>
                    <TableRow>
                      {canBulk && (
                        <TableHead className="w-10">
                          <Checkbox
                            aria-label={t("employees:bulk.selectPage")}
                            checked={pageFullySelected ? true : selectedOnPage > 0 ? "indeterminate" : false}
                            onCheckedChange={(checked) => togglePage(checked === true)}
                          />
                        </TableHead>
                      )}
                      <SortableHead label={t("employees:columns.name")} field="displayName" sort={sort} onSort={toggleSort} />
                      <TableHead>{t("employees:columns.orgUnit")}</TableHead>
                      <TableHead>{t("employees:columns.manager")}</TableHead>
                      <TableHead>{t("employees:columns.workEmail")}</TableHead>
                      <SortableHead label={t("employees:columns.status")} field="status" sort={sort} onSort={toggleSort} />
                      <TableHead>{t("employees:columns.accountStatus")}</TableHead>
                      <SortableHead label={t("employees:columns.hireDate")} field="hireDate" sort={sort} onSort={toggleSort} />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {items.map((employee) => (
                      <TableRow
                        key={employee.id}
                        className="cursor-pointer"
                        data-state={isSelected(employee.id) ? "selected" : undefined}
                        onClick={() => navigate(`/app/employees/${employee.id}`)}
                      >
                        {canBulk && (
                          <TableCell onClick={(e) => e.stopPropagation()}>
                            <Checkbox
                              aria-label={employee.displayName}
                              checked={isSelected(employee.id)}
                              onCheckedChange={(checked) => toggleRow(employee.id, checked === true)}
                            />
                          </TableCell>
                        )}
                        <TableCell className="font-medium">{employee.displayName}</TableCell>
                        <TableCell>{(employee.orgUnitId && unitNames.get(employee.orgUnitId)) || "—"}</TableCell>
                        <TableCell>{employee.managerDisplayName ?? "—"}</TableCell>
                        <TableCell>{employee.workEmail ?? "—"}</TableCell>
                        <TableCell>
                          <EmployeeStatusBadge status={employee.status} />
                        </TableCell>
                        <TableCell>
                          <AccountStatusBadge status={employee.accountStatus} />
                        </TableCell>
                        <TableCell className="whitespace-nowrap">
                          {employee.hireDate ? formatDateOnly(employee.hireDate, locale) : "—"}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
              {data && <PaginationFooter result={data} onPageChange={setPage} />}
            </>
          )}
        </CardContent>
      </Card>

      <CreateEmployeeDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  );
}

interface SortableHeadProps {
  label: string;
  field: EmployeeSortField;
  sort: { field: EmployeeSortField; desc: boolean };
  onSort: (field: EmployeeSortField) => void;
}

function SortableHead({ label, field, sort, onSort }: SortableHeadProps) {
  const active = sort.field === field;
  const Icon = !active ? ArrowUpDown : sort.desc ? ArrowDown : ArrowUp;
  return (
    <TableHead aria-sort={active ? (sort.desc ? "descending" : "ascending") : "none"}>
      <button type="button" className="inline-flex items-center gap-1 hover:text-foreground" onClick={() => onSort(field)}>
        {label}
        <Icon className={`h-3.5 w-3.5 ${active ? "" : "opacity-40"}`} />
      </button>
    </TableHead>
  );
}
