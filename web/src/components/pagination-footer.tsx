import { useTranslation } from "react-i18next";
import { Group, Pagination, Select, Text } from "@mantine/core";
import type { PagedResult } from "@/types";

const DEFAULT_PAGE_SIZE_OPTIONS = [10, 20, 50, 100];

interface PaginationFooterProps {
  result: Pick<PagedResult<unknown>, "page" | "totalPages" | "totalCount" | "hasNext" | "hasPrevious">;
  onPageChange: (page: number) => void;
  /** Shows the "rows per page" select when both are given. */
  pageSize?: number;
  onPageSizeChange?: (pageSize: number) => void;
  pageSizeOptions?: number[];
}

/** Shared pager, reused by every list screen (Locations, Positions, Users, Audit Log, data tables). */
export default function PaginationFooter({
  result,
  onPageChange,
  pageSize,
  onPageSizeChange,
  pageSizeOptions = DEFAULT_PAGE_SIZE_OPTIONS,
}: PaginationFooterProps) {
  const { t } = useTranslation(["common"]);

  if (result.totalCount === 0) return null;

  const totalPages = Math.max(1, result.totalPages, result.page);

  return (
    <Group justify="space-between" gap="sm" px="xs" py="sm">
      <Text size="sm" c="dimmed">
        {t("common:page")} {result.page} {t("common:of")} {result.totalPages} ({result.totalCount})
      </Text>
      <Group gap="sm" wrap="nowrap">
        {pageSize !== undefined && onPageSizeChange && (
          <Group gap="xs" wrap="nowrap">
            <Text size="sm" c="dimmed" visibleFrom="sm">
              {t("common:rowsPerPage")}
            </Text>
            <Select
              size="sm"
              w={80}
              aria-label={t("common:rowsPerPage")}
              data={pageSizeOptions.map(String)}
              value={String(pageSize)}
              allowDeselect={false}
              comboboxProps={{ withinPortal: true }}
              onChange={(value) => value && onPageSizeChange(Number(value))}
            />
          </Group>
        )}
        <Pagination
          size="sm"
          total={totalPages}
          value={result.page}
          siblings={1}
          boundaries={1}
          onChange={(page) => onPageChange(page)}
          getControlProps={(control) => {
            if (control === "previous") return { disabled: !result.hasPrevious };
            if (control === "next") return { disabled: !result.hasNext };
            return {};
          }}
        />
      </Group>
    </Group>
  );
}
