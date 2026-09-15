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
import { ArrowDown, ArrowUp, ArrowUpDown, Columns3 } from "lucide-react";
import PaginationFooter from "@/components/pagination-footer";
import { cn } from "@/lib/utils";
import { Button } from "./button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./dropdown-menu";
import { Skeleton } from "./skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./table";

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
  initialColumnVisibility = {},
  columnVisibilityKey,
  pageSize,
}: UseDataTableOptions<TData, TValue>): TanStackTable<TData> {
  const [localSorting, setLocalSorting] = React.useState<SortingState>([]);
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
  const clientPaged = table.options.getPaginationRowModel !== undefined;

  return (
    <div className={className}>
      <div className={cn(isFetching && "opacity-70")}>
        <Table containerClassName={cn(stickyHeader && "max-h-[70vh]")}>
          <TableHeader className={cn(stickyHeader && "sticky top-0 z-10 bg-card")}>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className="hover:bg-transparent">
                {headerGroup.headers.map((header) => {
                  const column = header.column;
                  const sorted = column.getIsSorted();
                  const content = header.isPlaceholder ? null : flexRender(column.columnDef.header, header.getContext());
                  return (
                    <TableHead
                      key={header.id}
                      className={column.columnDef.meta?.headerClassName}
                      aria-sort={column.getCanSort() ? (sorted === "desc" ? "descending" : sorted === "asc" ? "ascending" : "none") : undefined}
                    >
                      {column.getCanSort() ? (
                        <button
                          type="button"
                          className="inline-flex items-center gap-1 hover:text-foreground"
                          onClick={column.getToggleSortingHandler()}
                        >
                          {content}
                          {sorted === "desc" ? (
                            <ArrowDown className="h-3.5 w-3.5" />
                          ) : sorted === "asc" ? (
                            <ArrowUp className="h-3.5 w-3.5" />
                          ) : (
                            <ArrowUpDown className="h-3.5 w-3.5 opacity-40" />
                          )}
                        </button>
                      ) : (
                        content
                      )}
                    </TableHead>
                  );
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading ? (
              Array.from({ length: 5 }, (_, i) => (
                <TableRow key={i} className="hover:bg-transparent">
                  <TableCell colSpan={visibleColumnCount}>
                    <Skeleton className="h-6 w-full" />
                  </TableCell>
                </TableRow>
              ))
            ) : rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={visibleColumnCount} className="p-6 text-center text-sm text-muted-foreground">
                  {emptyMessage}
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row) => (
                <TableRow
                  key={row.id}
                  className={cn(onRowClick && "cursor-pointer")}
                  data-state={isRowSelected?.(row.original) ? "selected" : undefined}
                  onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id} className={cell.column.columnDef.meta?.cellClassName}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>
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
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" className={className}>
          <Columns3 className="mr-2 h-4 w-4" /> {t("common:columns")}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel>{t("common:visibleColumns")}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {hideable.map((column) => (
          <DropdownMenuCheckboxItem
            key={column.id}
            checked={column.getIsVisible()}
            onCheckedChange={(checked) => column.toggleVisibility(checked === true)}
            onSelect={(event) => event.preventDefault()}
          >
            {column.columnDef.meta?.label ?? column.id}
          </DropdownMenuCheckboxItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => table.resetColumnVisibility()}>{t("common:resetColumns")}</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
