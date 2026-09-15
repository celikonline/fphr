import * as React from "react";
import { useTranslation } from "react-i18next";
import {
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type RowData,
  type SortingState,
  type Table as TanStackTable,
  type Updater,
  type VisibilityState,
} from "@tanstack/react-table";
import { Box, Button, Group, Menu, Skeleton, Table, Text, UnstyledButton } from "@mantine/core";
import { ArrowDown, ArrowUp, ArrowUpDown, Columns3 } from "lucide-react";
import PaginationFooter from "@/components/pagination-footer";

/** Smallest option of PaginationFooter's rows-per-page select. */
const MIN_PAGE_SIZE_OPTION = 10;

declare module "@tanstack/react-table" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Column chooser label; defaults to the column id. */
    label?: string;
    headerClassName?: string;
    cellClassName?: string;
  }
}

export interface UseDataTableOptions<TData, TValue> {
  columns: ColumnDef<TData, TValue>[];
  data: TData[];
  getRowId?: (row: TData) => string;
  /** Controlled sorting. Passing `onSortingChange` switches to manual (server-side) sorting. */
  sorting?: SortingState;
  onSortingChange?: (sorting: SortingState) => void;
  /** Starting sort for uncontrolled (client-side) sorting. */
  initialSorting?: SortingState;
  /** Columns hidden until the user shows them from the column chooser. */
  initialColumnVisibility?: VisibilityState;
  /** localStorage key that remembers the user's column choice per browser. */
  columnVisibilityKey?: string;
  /** Client-side paging page size; omit for server-paged or unpaged data. */
  pageSize?: number;
}

function readVisibility(key: string | undefined): VisibilityState | null {
  if (!key) return null;
  try {
    const raw = localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as VisibilityState) : null;
  } catch {
    return null;
  }
}

function resolve<T>(updater: Updater<T>, current: T): T {
  return typeof updater === "function" ? (updater as (old: T) => T)(current) : updater;
}

/** TanStack Table instance with the app's defaults (asc-first sorting, persisted column visibility). */
export function useDataTable<TData, TValue>({
  columns,
  data,
  getRowId,
  sorting: controlledSorting,
  onSortingChange,
  initialSorting = [],
  initialColumnVisibility = {},
  columnVisibilityKey,
  pageSize,
}: UseDataTableOptions<TData, TValue>): TanStackTable<TData> {
  const [localSorting, setLocalSorting] = React.useState<SortingState>(initialSorting);
  const [columnVisibility, setColumnVisibility] = React.useState<VisibilityState>(
    () => readVisibility(columnVisibilityKey) ?? initialColumnVisibility
  );
  const sorting = controlledSorting ?? localSorting;

  // The table instance is rebuilt every render by design; this app does not run the React Compiler.
  // eslint-disable-next-line react-hooks/incompatible-library
  return useReactTable({
    columns,
    data,
    getRowId: getRowId ? (row) => getRowId(row) : undefined,
    state: { sorting, columnVisibility },
    manualSorting: onSortingChange !== undefined,
    sortDescFirst: false,
    enableSortingRemoval: false,
    onSortingChange: (updater) => {
      const next = resolve(updater, sorting);
      if (onSortingChange) onSortingChange(next);
      else setLocalSorting(next);
    },
    onColumnVisibilityChange: (updater) => {
      setColumnVisibility((current) => {
        const next = resolve(updater, current);
        if (columnVisibilityKey) {
          try {
            localStorage.setItem(columnVisibilityKey, JSON.stringify(next));
          } catch {
            // Storage unavailable (private mode); the choice just isn't remembered.
          }
        }
        return next;
      });
    },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: onSortingChange ? undefined : getSortedRowModel(),
    getPaginationRowModel: pageSize ? getPaginationRowModel() : undefined,
    initialState: pageSize ? { pagination: { pageIndex: 0, pageSize } } : undefined,
  });
}

interface DataTableProps<TData> {
  table: TanStackTable<TData>;
  isLoading?: boolean;
  /** Dims the rows while a background refetch (new page/sort) is running. */
  isFetching?: boolean;
  emptyMessage?: React.ReactNode;
  onRowClick?: (row: TData) => void;
  isRowSelected?: (row: TData) => boolean;
  /** Keeps the header visible while the body scrolls inside a height-limited container. */
  stickyHeader?: boolean;
  className?: string;
}

const SORT_ICON_SIZE = 14;

export function DataTable<TData>({
  table,
  isLoading,
  isFetching,
  emptyMessage,
  onRowClick,
  isRowSelected,
  stickyHeader,
  className,
}: DataTableProps<TData>) {
  const rows = table.getRowModel().rows;
  const visibleColumnCount = table.getVisibleLeafColumns().length;
  // Client pager only once the rows exceed the smallest rows-per-page option; a handful of rows needs no footer.
  const clientPaged =
    table.options.getPaginationRowModel !== undefined && table.getPrePaginationRowModel().rows.length > MIN_PAGE_SIZE_OPTION;

  return (
    <div className={className}>
      <Box
        opacity={isFetching ? 0.7 : undefined}
        style={{ overflow: "auto", maxHeight: stickyHeader ? "70vh" : undefined, position: "relative", width: "100%" }}
      >
        <Table stickyHeader={stickyHeader} highlightOnHover={!isLoading && rows.length > 0} verticalSpacing="sm" fz="sm">
          <Table.Thead>
            {table.getHeaderGroups().map((headerGroup) => (
              <Table.Tr key={headerGroup.id} data-hover={undefined}>
                {headerGroup.headers.map((header) => {
                  const column = header.column;
                  const sorted = column.getIsSorted();
                  const content = header.isPlaceholder ? null : flexRender(column.columnDef.header, header.getContext());
                  return (
                    <Table.Th
                      key={header.id}
                      className={column.columnDef.meta?.headerClassName}
                      c="dimmed"
                      fw={500}
                      style={{ whiteSpace: "nowrap" }}
                      aria-sort={column.getCanSort() ? (sorted === "desc" ? "descending" : sorted === "asc" ? "ascending" : "none") : undefined}
                    >
                      {column.getCanSort() ? (
                        <UnstyledButton type="button" fz="sm" fw={500} c="inherit" onClick={column.getToggleSortingHandler()}>
                          <Group component="span" gap={4} wrap="nowrap">
                            {content}
                            {sorted === "desc" ? (
                              <ArrowDown size={SORT_ICON_SIZE} />
                            ) : sorted === "asc" ? (
                              <ArrowUp size={SORT_ICON_SIZE} />
                            ) : (
                              <ArrowUpDown size={SORT_ICON_SIZE} opacity={0.4} />
                            )}
                          </Group>
                        </UnstyledButton>
                      ) : (
                        content
                      )}
                    </Table.Th>
                  );
                })}
              </Table.Tr>
            ))}
          </Table.Thead>
          <Table.Tbody>
            {isLoading ? (
              Array.from({ length: 5 }, (_, i) => (
                <Table.Tr key={i}>
                  <Table.Td colSpan={visibleColumnCount}>
                    <Skeleton height={24} />
                  </Table.Td>
                </Table.Tr>
              ))
            ) : rows.length === 0 ? (
              <Table.Tr>
                <Table.Td colSpan={visibleColumnCount} p="lg">
                  <Text size="sm" c="dimmed" ta="center">
                    {emptyMessage}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ) : (
              rows.map((row) => {
                const selected = isRowSelected?.(row.original) ?? false;
                return (
                  <Table.Tr
                    key={row.id}
                    data-selected={selected || undefined}
                    bg={selected ? "var(--mantine-primary-color-light)" : undefined}
                    style={onRowClick ? { cursor: "pointer" } : undefined}
                    onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                  >
                    {row.getVisibleCells().map((cell) => (
                      <Table.Td key={cell.id} className={cell.column.columnDef.meta?.cellClassName}>
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </Table.Td>
                    ))}
                  </Table.Tr>
                );
              })
            )}
          </Table.Tbody>
        </Table>
      </Box>
      {clientPaged && !isLoading && (
        <PaginationFooter
          result={{
            page: table.getState().pagination.pageIndex + 1,
            totalPages: Math.max(1, table.getPageCount()),
            totalCount: table.getPrePaginationRowModel().rows.length,
            hasNext: table.getCanNextPage(),
            hasPrevious: table.getCanPreviousPage(),
          }}
          onPageChange={(page) => table.setPageIndex(page - 1)}
          pageSize={table.getState().pagination.pageSize}
          onPageSizeChange={(size) => table.setPageSize(size)}
        />
      )}
    </div>
  );
}

/** "Kolonlar" menu: toggles every hideable column; the choice is remembered when the table has a `columnVisibilityKey`. */
export function DataTableViewOptions<TData>({ table, className }: { table: TanStackTable<TData>; className?: string }) {
  const { t } = useTranslation(["common"]);
  const hideable = table.getAllLeafColumns().filter((column) => column.getCanHide());

  if (hideable.length === 0) return null;

  return (
    <Menu position="bottom-end" width={224} closeOnItemClick={false} shadow="md">
      <Menu.Target>
        <Button type="button" variant="default" className={className} leftSection={<Columns3 size={16} />}>
          {t("common:columns")}
        </Button>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t("common:visibleColumns")}</Menu.Label>
        <Menu.Divider />
        {hideable.map((column) => (
          <Menu.CheckboxItem
            key={column.id}
            checked={column.getIsVisible()}
            onChange={(checked) => column.toggleVisibility(checked)}
          >
            {column.columnDef.meta?.label ?? column.id}
          </Menu.CheckboxItem>
        ))}
        <Menu.Divider />
        <Menu.Item closeMenuOnClick onClick={() => table.resetColumnVisibility()}>
          {t("common:resetColumns")}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}
