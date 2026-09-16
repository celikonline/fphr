import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import {
  Anchor,
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Input,
  Paper,
  SegmentedControl,
  Select,
  Switch,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { GitBranch, LayoutGrid, List, Plus, Search, Users } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
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
import EmployeeCardGrid from "@/components/employees/employee-card-grid";
import { CONTRACT_TYPE_OPTIONS, EMPLOYMENT_TYPE_OPTIONS } from "@/components/employees/employee-enum-options";
import EmployeeDirectory from "@/components/employees/employee-directory";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";

const DEFAULT_PAGE_SIZE = 20;
const COLUMN_VISIBILITY_KEY = "senseik.employees.grid.columns";
const VIEW_MODE_KEY = "senseik.employees.viewMode";
const ALL = "__all__";

type ViewMode = "list" | "grid";

function readViewMode(): ViewMode {
  try {
    return localStorage.getItem(VIEW_MODE_KEY) === "grid" ? "grid" : "list";
  } catch {
    return "list";
  }
}
const BULK_PERMISSIONS = [...new Set(BULK_ACTIONS.map((a) => a.permission))];
/** Small dimmed filter labels, matching the org unit filter's `Input.Label`. */
const FILTER_LABEL_STYLES = {
  label: { fontSize: "var(--mantine-font-size-xs)", fontWeight: 400, color: "var(--mantine-color-dimmed)" },
};

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
  const [employmentType, setEmploymentType] = useState(ALL);
  const [contractType, setContractType] = useState(ALL);
  const [missingInfo, setMissingInfo] = useState(false);
  const [sort, setSort] = useState<{ field: EmployeeSortField; desc: boolean }>({ field: "displayName", desc: false });
  const [selection, setSelection] = useState<SelectionState>(EMPTY_SELECTION);
  const [createOpen, setCreateOpen] = useState(false);
  const [viewMode, setViewMode] = useState<ViewMode>(readViewMode);

  function changeViewMode(next: ViewMode) {
    setViewMode(next);
    try {
      localStorage.setItem(VIEW_MODE_KEY, next);
    } catch {
      // Storage unavailable (private mode): the choice just isn't remembered.
    }
  }

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
      employmentType: employmentType === ALL ? null : employmentType,
      contractType: contractType === ALL ? null : contractType,
      missingInfo: missingInfo ? true : null,
    }),
    [debouncedSearch, orgUnitId, includeSubUnits, status, account, employmentType, contractType, missingInfo]
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
        employmentType: filter.employmentType,
        contractType: filter.contractType,
        missingInfo: filter.missingInfo,
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
                checked={pageFullySelected}
                indeterminate={!pageFullySelected && selectedOnPage > 0}
                onChange={(e) => togglePage(e.currentTarget.checked)}
              />
            ),
            cell: ({ row }) => (
              <div onClick={(e) => e.stopPropagation()}>
                <Checkbox
                  aria-label={row.original.displayName}
                  checked={isSelected(row.original.id)}
                  onChange={(e) => toggleRow(row.original.id, e.currentTarget.checked)}
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
      meta: { label: t("employees:columns.name") },
      cell: ({ row }) => (
        <Group gap="xs" wrap="nowrap">
          <Text size="sm" fw={500}>
            {row.original.displayName}
          </Text>
          {row.original.hasMissingInfo && (
            <Tooltip label={t("employees:filters.missingInfoHint")}>
              <Badge size="xs" variant="light" color="orange">
                {t("employees:filters.missingInfo")}
              </Badge>
            </Tooltip>
          )}
        </Group>
      ),
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

  const hasActiveFilters =
    filter.q || filter.orgUnitId || filter.status || filter.hasUserAccount !== null || filter.employmentType || filter.contractType || filter.missingInfo;

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2">
            <Group component="span" gap="xs">
              <Users size={24} /> {t("employees:title")}
            </Group>
          </Title>
          <Text c="dimmed">{t("employees:subtitle")}</Text>
        </div>
        <Group gap="xs">
          {hasOrgChartPermission && orgChartEnabled && (
            <Button component={Link} to="/app/employees/org-chart" variant="default" leftSection={<GitBranch size={16} />}>
              {t("employees:orgChart.title")}
            </Button>
          )}
          {canCreate && (
            <Button type="button" onClick={() => setCreateOpen(true)} leftSection={<Plus size={16} />}>
              {t("employees:create.button")}
            </Button>
          )}
        </Group>
      </Group>

      <Group align="flex-end" gap="sm">
        <TextInput
          className="w-full max-w-xs"
          leftSection={<Search size={16} />}
          placeholder={t("employees:searchPlaceholder")}
          aria-label={t("employees:searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        <div className="w-56">
          <Input.Label htmlFor="emp-filter-unit" size="xs" c="dimmed" fw={400}>
            {t("employees:filters.orgUnit")}
          </Input.Label>
          <OrgUnitSelect id="emp-filter-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
        </div>
        {orgUnitId && (
          <div className="flex h-9 items-center">
            <Switch
              id="emp-filter-sub"
              checked={includeSubUnits}
              onChange={(e) => setIncludeSubUnits(e.currentTarget.checked)}
              label={t("employees:filters.includeSubUnits")}
            />
          </div>
        )}
        <Select
          id="emp-filter-status"
          w={160}
          label={t("employees:filters.status")}
          styles={FILTER_LABEL_STYLES}
          value={status}
          onChange={(value) => setStatus(value ?? ALL)}
          allowDeselect={false}
          data={[
            { value: ALL, label: t("employees:filters.all") },
            ...EMPLOYEE_STATUSES.map((s) => ({ value: s, label: t(`employees:status.${s}`) })),
          ]}
        />
        <Select
          id="emp-filter-account"
          w={176}
          label={t("employees:filters.hasUserAccount")}
          styles={FILTER_LABEL_STYLES}
          value={account}
          onChange={(value) => setAccount(value ?? ALL)}
          allowDeselect={false}
          data={[
            { value: ALL, label: t("employees:filters.all") },
            { value: "yes", label: t("employees:filters.withAccount") },
            { value: "no", label: t("employees:filters.withoutAccount") },
          ]}
        />
        <Select
          id="emp-filter-employmentType"
          w={176}
          label={t("employees:fields.employmentType")}
          styles={FILTER_LABEL_STYLES}
          value={employmentType}
          onChange={(value) => setEmploymentType(value ?? ALL)}
          allowDeselect={false}
          data={[
            { value: ALL, label: t("employees:filters.all") },
            ...EMPLOYMENT_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.employmentType.${option}`) })),
          ]}
        />
        <Select
          id="emp-filter-contractType"
          w={176}
          label={t("employees:fields.contractType")}
          styles={FILTER_LABEL_STYLES}
          value={contractType}
          onChange={(value) => setContractType(value ?? ALL)}
          allowDeselect={false}
          data={[
            { value: ALL, label: t("employees:filters.all") },
            ...CONTRACT_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.contractType.${option}`) })),
          ]}
        />
        <div className="flex h-9 items-center">
          <Switch
            id="emp-filter-missingInfo"
            checked={missingInfo}
            onChange={(e) => setMissingInfo(e.currentTarget.checked)}
            label={t("employees:filters.missingInfo")}
          />
        </div>
        <Group gap="xs" ml="auto">
          {viewMode === "list" && <DataTableViewOptions table={table} />}
          <SegmentedControl
            aria-label={t("employees:viewMode.label")}
            value={viewMode}
            onChange={(value) => changeViewMode(value as ViewMode)}
            data={[
              {
                value: "list",
                label: (
                  <Tooltip label={t("employees:viewMode.list")}>
                    <span className="flex items-center" aria-label={t("employees:viewMode.list")}>
                      <List size={16} />
                    </span>
                  </Tooltip>
                ),
              },
              {
                value: "grid",
                label: (
                  <Tooltip label={t("employees:viewMode.grid")}>
                    <span className="flex items-center" aria-label={t("employees:viewMode.grid")}>
                      <LayoutGrid size={16} />
                    </span>
                  </Tooltip>
                ),
              },
            ]}
          />
        </Group>
      </Group>

      {canBulk && (
        <Group gap="sm">
          {viewMode === "grid" && items.length > 0 && (
            <Checkbox
              aria-label={t("employees:bulk.selectPage")}
              checked={pageFullySelected}
              indeterminate={!pageFullySelected && selectedOnPage > 0}
              onChange={(e) => togglePage(e.currentTarget.checked)}
            />
          )}
          <BulkActionsMenu
            selection={apiSelection}
            selectedCount={selectedCount}
            nameById={nameById}
            onCompleted={onBulkCompleted}
          />
          <Text size="sm" c="dimmed">
            {selectedCount > 0 ? t("employees:bulk.selectedCount", { count: selectedCount }) : t("employees:bulk.selectHint")}
          </Text>
          {selectedCount > 0 && (
            <Anchor component="button" type="button" size="sm" onClick={() => setSelection(EMPTY_SELECTION)}>
              {t("employees:bulk.clearSelection")}
            </Anchor>
          )}
        </Group>
      )}

      {canBulk && selection.mode === "ids" && pageFullySelected && totalCount > selection.ids.size && (
        <Paper withBorder px="md" py="xs" bg="var(--mantine-color-default-hover)">
          <Group gap="xs">
            <Text size="sm">{t("employees:bulk.pageSelected", { count: selection.ids.size })}</Text>
            <Anchor component="button" type="button" size="sm" onClick={() => setSelection({ mode: "all", excluded: new Set() })}>
              {t("employees:bulk.selectAllMatching", { count: totalCount })}
            </Anchor>
          </Group>
        </Paper>
      )}
      {canBulk && selection.mode === "all" && (
        <Paper
          withBorder
          px="md"
          py="xs"
          bg="var(--mantine-primary-color-light)"
          style={{ borderColor: "var(--mantine-primary-color-light-color)" }}
        >
          <Text size="sm">
            {t("employees:bulk.allMatchingSelected", { count: selectedCount })}
            {selection.excluded.size > 0 && ` ${t("employees:bulk.excludedCount", { count: selection.excluded.size })}`}
          </Text>
        </Paper>
      )}

      <Card withBorder padding={0}>
        {viewMode === "grid" ? (
          <EmployeeCardGrid
            items={items}
            isLoading={isLoading}
            isFetching={isFetching}
            emptyMessage={hasActiveFilters ? t("employees:emptyFiltered") : t("employees:empty")}
            unitNames={unitNames}
            onOpen={(employee) => navigate(`/app/employees/${employee.id}`)}
            isSelected={isSelected}
            onToggle={canBulk ? toggleRow : undefined}
          />
        ) : (
          <DataTable
            table={table}
            isLoading={isLoading}
            isFetching={isFetching}
            stickyHeader
            emptyMessage={hasActiveFilters ? t("employees:emptyFiltered") : t("employees:empty")}
            isRowSelected={(employee) => isSelected(employee.id)}
            onRowClick={(employee) => navigate(`/app/employees/${employee.id}`)}
          />
        )}
        {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
      </Card>

      <CreateEmployeeDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  );
}
