import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { Card, Stack, Tabs, TextInput } from "@mantine/core";
import { Search } from "lucide-react";
import PaginationFooter from "@/components/pagination-footer";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import type { ReportCell, ReportColumnType, ReportData } from "@/services/reports.service";

type Row = ReportCell[];

interface ReportDataGridProps {
  data: ReportData | undefined;
  isLoading: boolean;
  isFetching: boolean;
  search: string;
  onSearchChange: (value: string) => void;
  /** Column id is the column index ("0", "1", …); the server sorts by it. */
  sorting: SortingState;
  onSortingChange: (sorting: SortingState) => void;
  onSheetChange: (index: number) => void;
  onPageChange: (page: number) => void;
  pageSize: number;
  onPageSizeChange: (size: number) => void;
}

const DATE_ONLY_PREFIX = /^\d{4}-\d{2}-\d{2}/;

/** Workbook cells are untrusted text: an unparseable date must show as-is, not throw during render. */
function formatDateCell(value: string, format: (text: string) => string): string {
  try {
    return format(value);
  } catch {
    return value;
  }
}

function formatCell(
  value: ReportCell,
  type: ReportColumnType,
  locale: string,
  yes: string,
  no: string
): string {
  if (value === null || value === undefined) return "";
  switch (type) {
    case "number":
      return typeof value === "number"
        ? new Intl.NumberFormat(locale).format(value)
        : String(value);
    case "date":
      // A time part ("2024-03-01T00:00:00") still shows as its calendar date.
      return formatDateCell(String(value), (raw) =>
        formatDateOnly(
          DATE_ONLY_PREFIX.test(raw) ? raw.slice(0, 10) : raw,
          locale
        )
      );
    case "dateTime":
      return formatDateCell(String(value), (raw) =>
        formatDateTime(raw, locale)
      );
    case "boolean":
      return value === true ? yes : value === false ? no : String(value);
    default:
      return String(value);
  }
}

/** Generic report grid: sheet tabs, search, server-sorted/paged table whose columns come from the workbook's header row. */
export function ReportDataGrid({
  data,
  isLoading,
  isFetching,
  search,
  onSearchChange,
  sorting,
  onSortingChange,
  onSheetChange,
  onPageChange,
  pageSize,
  onPageSizeChange,
}: ReportDataGridProps) {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";
  const yes = t("common:yes");
  const no = t("common:no");

  const columns = useMemo<ColumnDef<Row>[]>(
    () =>
      (data?.columns ?? []).map((column, index) => ({
        id: String(index),
        header: column.header || `#${index + 1}`,
        accessorFn: (row) => row[index] ?? null,
        meta: {
          label: column.header,
          headerClassName: column.type === "number" ? "text-right" : undefined,
          cellClassName: column.type === "number" ? "text-right tabular-nums" : undefined,
        },
        cell: ({ getValue }) => formatCell(getValue() as ReportCell, column.type, locale, yes, no),
      })),
    [data?.columns, locale, yes, no]
  );

  const table = useDataTable({
    columns,
    data: data?.rows.items ?? [],
    sorting,
    onSortingChange,
  });

  return (
    <Stack gap="sm">
      {data && data.sheets.length > 1 && (
        <Tabs
          value={String(data.sheetIndex)}
          onChange={(value) => value !== null && onSheetChange(Number(value))}
        >
          <Tabs.List>
            {data.sheets.map((sheet) => (
              <Tabs.Tab key={sheet.index} value={String(sheet.index)}>
                {sheet.name}
              </Tabs.Tab>
            ))}
          </Tabs.List>
        </Tabs>
      )}

      <TextInput
        aria-label={t("reports:viewer.search")}
        placeholder={t("reports:viewer.search")}
        leftSection={<Search size={14} />}
        value={search}
        onChange={(event) => onSearchChange(event.currentTarget.value)}
        maw={360}
      />

      <Card withBorder padding={0} radius="md">
        <DataTable
          table={table}
          isLoading={isLoading}
          isFetching={isFetching}
          emptyMessage={t("reports:viewer.noRows")}
          stickyHeader
        />
      </Card>

      {data && (
        <PaginationFooter
          result={data.rows}
          onPageChange={onPageChange}
          pageSize={pageSize}
          onPageSizeChange={onPageSizeChange}
        />
      )}
    </Stack>
  );
}
