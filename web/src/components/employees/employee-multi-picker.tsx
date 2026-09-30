import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Button, Checkbox, Group, Paper, ScrollArea, Stack, Switch, Text } from "@mantine/core";
import { listEmployees } from "@/services/employees.service";
import type { EmployeeFilter, EmployeeSelection } from "@/types";
import EmployeeFilterPanel from "./employee-filter-panel";

const PAGE_SIZE = 50;
const SEARCH_DEBOUNCE_MS = 300;

type SelectionState =
  | { mode: "ids"; picked: ReadonlyMap<string, string> }
  | { mode: "all"; excluded: ReadonlySet<string> };

const EMPTY: SelectionState = { mode: "ids", picked: new Map() };

interface EmployeeMultiPickerProps {
  onConfirm: (selection: EmployeeSelection) => void;
  /** Filter the picker starts with. */
  initialFilter?: EmployeeFilter;
  /** Optional cap: shows a live "count/max" warning and disables confirm while the selection exceeds it. */
  maxSelection?: number;
}

/**
 * Multi-employee picker with filters. Produces an {@link EmployeeSelection}: explicit ids, or
 * "select all matching" (filter + excluded ids). Explicit picks survive filter changes; a
 * "select all" is tied to its filter, so changing the filter resets it.
 */
export default function EmployeeMultiPicker({ onConfirm, initialFilter, maxSelection }: EmployeeMultiPickerProps) {
  const { t } = useTranslation(["employees", "common"]);
  const [filter, setFilter] = useState<EmployeeFilter>(initialFilter ?? {});
  const [debouncedFilter, setDebouncedFilter] = useState(filter);
  const [limit, setLimit] = useState(PAGE_SIZE);
  const [keepSelected, setKeepSelected] = useState(true);
  const [selection, setSelection] = useState<SelectionState>(EMPTY);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedFilter(filter), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [filter]);

  const [prevFilter, setPrevFilter] = useState(debouncedFilter);
  if (prevFilter !== debouncedFilter) {
    setPrevFilter(debouncedFilter);
    setLimit(PAGE_SIZE);
    if (selection.mode === "all") setSelection(EMPTY);
  }

  const { data, isLoading } = useQuery({
    queryKey: ["employees", "multi-picker", debouncedFilter, limit],
    queryFn: () =>
      listEmployees({
        ...debouncedFilter,
        q: debouncedFilter.q ?? undefined,
        page: 1,
        pageSize: limit,
        sort: "displayName",
      }),
    placeholderData: (previous) => previous,
  });

  const items = useMemo(() => data?.items ?? [], [data]);
  const totalCount = data?.totalCount ?? 0;

  const rows = useMemo(() => {
    const base = items.map((e) => ({ id: e.id, name: e.displayName }));
    if (!keepSelected || selection.mode !== "ids") return base;
    const shown = new Set(base.map((r) => r.id));
    const kept = [...selection.picked].filter(([id]) => !shown.has(id)).map(([id, name]) => ({ id, name }));
    return [...kept, ...base];
  }, [items, keepSelected, selection]);

  const isChecked = (id: string) => (selection.mode === "ids" ? selection.picked.has(id) : !selection.excluded.has(id));
  const selectedCount =
    selection.mode === "ids" ? selection.picked.size : Math.max(0, totalCount - selection.excluded.size);

  function toggle(id: string, name: string, checked: boolean) {
    setSelection((prev) => {
      if (prev.mode === "ids") {
        const picked = new Map(prev.picked);
        if (checked) picked.set(id, name);
        else picked.delete(id);
        return { mode: "ids", picked };
      }
      const excluded = new Set(prev.excluded);
      if (checked) excluded.delete(id);
      else excluded.add(id);
      return { mode: "all", excluded };
    });
  }

  const overLimit = maxSelection !== undefined && selectedCount > maxSelection;

  function confirm() {
    if (selectedCount === 0) return;
    onConfirm(
      selection.mode === "ids"
        ? { employeeIds: [...selection.picked.keys()], filter: null, excludedEmployeeIds: null }
        : {
            employeeIds: null,
            filter: { ...debouncedFilter, includeTerminated: debouncedFilter.includeTerminated ?? false },
            excludedEmployeeIds: [...selection.excluded],
          }
    );
  }

  return (
    <Stack gap="sm">
      <EmployeeFilterPanel value={filter} onChange={setFilter} />
      <Group justify="space-between">
        <Button
          variant="light"
          size="xs"
          disabled={totalCount === 0}
          onClick={() => setSelection({ mode: "all", excluded: new Set() })}
        >
          {t("employees:multiPicker.selectAll", { count: totalCount })}
        </Button>
        <Switch
          size="xs"
          label={t("employees:multiPicker.keepSelected")}
          checked={keepSelected}
          onChange={(e) => setKeepSelected(e.currentTarget.checked)}
        />
      </Group>
      <Paper withBorder p="xs">
        <ScrollArea.Autosize mah={320}>
          {rows.length === 0 && !isLoading ? (
            <Text c="dimmed" size="sm">
              {t("employees:multiPicker.empty")}
            </Text>
          ) : (
            <Stack gap={6}>
              {rows.map((row) => (
                <Checkbox
                  key={row.id}
                  label={row.name}
                  checked={isChecked(row.id)}
                  onChange={(e) => toggle(row.id, row.name, e.currentTarget.checked)}
                />
              ))}
            </Stack>
          )}
        </ScrollArea.Autosize>
        {items.length < totalCount && (
          <Button variant="subtle" size="xs" mt="xs" onClick={() => setLimit((n) => n + PAGE_SIZE)}>
            {t("employees:multiPicker.loadMore")}
          </Button>
        )}
      </Paper>
      {overLimit && (
        <Text size="sm" c="red" role="alert" data-testid="multi-picker-limit">
          {t("employees:multiPicker.overLimit", { max: maxSelection, count: selectedCount })}
        </Text>
      )}
      <Group justify="space-between">
        <Group gap="xs">
          <Badge variant="light" color={overLimit ? "red" : undefined}>
            {selection.mode === "all"
              ? t("employees:multiPicker.allSelectedExcluding", { count: selection.excluded.size })
              : t("employees:multiPicker.selectedCount", { count: selectedCount })}
          </Badge>
          <Button variant="subtle" size="xs" disabled={selectedCount === 0} onClick={() => setSelection(EMPTY)}>
            {t("employees:multiPicker.clear")}
          </Button>
        </Group>
        <Button disabled={selectedCount === 0 || overLimit} onClick={confirm}>
          {t("employees:multiPicker.confirm")}
        </Button>
      </Group>
    </Stack>
  );
}
