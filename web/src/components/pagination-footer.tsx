import { useTranslation } from "react-i18next";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { PagedResult } from "@/types";

interface PaginationFooterProps {
  result: Pick<PagedResult<unknown>, "page" | "totalPages" | "totalCount" | "hasNext" | "hasPrevious">;
  onPageChange: (page: number) => void;
}

/** Shared prev/next pager, reused by every Item 20 list screen (Locations, Positions, Users, Audit Log). */
export default function PaginationFooter({ result, onPageChange }: PaginationFooterProps) {
  const { t } = useTranslation(["common"]);

  if (result.totalCount === 0) return null;

  return (
    <div className="flex items-center justify-between px-2 py-3">
      <p className="text-sm text-muted-foreground">
        {t("common:page")} {result.page} {t("common:of")} {result.totalPages} ({result.totalCount})
      </p>
      <div className="flex gap-2">
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
