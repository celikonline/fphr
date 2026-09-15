import { useTranslation } from "react-i18next";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
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

/** Shared prev/next pager, reused by every Item 20 list screen (Locations, Positions, Users, Audit Log). */
export default function PaginationFooter({
  result,
  onPageChange,
  pageSize,
  onPageSizeChange,
  pageSizeOptions = DEFAULT_PAGE_SIZE_OPTIONS,
}: PaginationFooterProps) {
  const { t } = useTranslation(["common"]);

  if (result.totalCount === 0) return null;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 px-2 py-3">
      <p className="text-sm text-muted-foreground">
        {t("common:page")} {result.page} {t("common:of")} {result.totalPages} ({result.totalCount})
      </p>
      <div className="flex items-center gap-2">
        {pageSize !== undefined && onPageSizeChange && (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <span className="hidden sm:inline">{t("common:rowsPerPage")}</span>
            <Select value={String(pageSize)} onValueChange={(value) => onPageSizeChange(Number(value))}>
              <SelectTrigger className="h-9 w-20" aria-label={t("common:rowsPerPage")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {pageSizeOptions.map((option) => (
                  <SelectItem key={option} value={String(option)}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!result.hasPrevious}
          onClick={() => onPageChange(result.page - 1)}
        >
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!result.hasNext}
          onClick={() => onPageChange(result.page + 1)}
        >
          <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
