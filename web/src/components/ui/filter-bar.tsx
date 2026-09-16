import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button, Card, Group, TextInput } from "@mantine/core";
import { Search, X } from "lucide-react";

export interface FilterBarSearchProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  label?: string;
  id?: string;
}

export interface FilterBarProps {
  /** Optional leading search box; omit for filter bars with no free-text search. */
  search?: FilterBarSearchProps;
  /** The select/date-range/etc. controls specific to the page - `FilterBar` only owns the layout + search + clear. */
  children?: ReactNode;
  hasActiveFilters: boolean;
  onClearFilters: () => void;
  /** Right-aligned slot, e.g. `<DataTableViewOptions />`. */
  trailing?: ReactNode;
  className?: string;
}

/**
 * Faz 1 ortak bileşen: the search + filter-controls + "clear filters" row shared by grid pages
 * (`audit-log.tsx` is the first consumer). Each page still owns its own filter state and controls
 * (Select/DatePicker/...) - passed as `children` - `FilterBar` only standardizes the wrapping layout.
 */
export function FilterBar({ search, children, hasActiveFilters, onClearFilters, trailing, className }: FilterBarProps) {
  const { t } = useTranslation(["common"]);

  return (
    <Card withBorder padding="md" className={className}>
      <Group align="flex-end" gap="sm">
        {search && (
          <TextInput
            id={search.id}
            className="min-w-[14rem] flex-1"
            label={search.label ?? t("common:search")}
            leftSection={<Search className="h-4 w-4" />}
            placeholder={search.placeholder}
            value={search.value}
            onChange={(e) => search.onChange(e.currentTarget.value)}
          />
        )}
        {children}
        {hasActiveFilters && (
          <Button type="button" variant="subtle" size="sm" leftSection={<X className="h-4 w-4" />} onClick={onClearFilters}>
            {t("common:clearFilters")}
          </Button>
        )}
        {trailing}
      </Group>
    </Card>
  );
}
