import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Check, Search } from "lucide-react";
import { Combobox, Group, InputBase, ScrollArea, Text, useCombobox } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import { listEmployees } from "@/services/employees.service";

export interface PickedEmployee {
  id: string;
  displayName: string;
}

interface EmployeePickerProps {
  id?: string;
  value: PickedEmployee | null;
  onChange: (value: PickedEmployee | null) => void;
  /** Employee that must not be offered (e.g. the card's own employee as its manager). */
  excludeId?: string;
  placeholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
  /** Optional field label/description/error rendered by the Mantine input wrapper. */
  label?: ReactNode;
  description?: ReactNode;
  error?: ReactNode;
  withAsterisk?: boolean;
}

/** Searchable single-employee combobox backed by `GET /v1/employees?q` (active employees). */
export default function EmployeePicker({
  id,
  value,
  onChange,
  excludeId,
  placeholder,
  allowClear,
  disabled,
  label,
  description,
  error,
  withAsterisk,
}: EmployeePickerProps) {
  const { t } = useTranslation(["employees"]);
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 250);

  const combobox = useCombobox({
    onDropdownOpen: () => combobox.focusSearchInput(),
    onDropdownClose: () => {
      combobox.resetSelectedOption();
      combobox.focusTarget();
    },
  });
  const open = combobox.dropdownOpened;

  const { data, isFetching } = useQuery({
    queryKey: ["employees", "picker", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 10, q: debounced || undefined, status: "Active", sort: "displayName" }),
    enabled: open,
  });

  const items = (data?.items ?? []).filter((e) => e.id !== excludeId);
  const resultKey = items.map((e) => e.id).join(",");

  // Like the previous cmdk list: the first result is highlighted so Enter picks it right away.
  const { selectFirstOption } = combobox;
  useEffect(() => {
    if (open) selectFirstOption();
  }, [open, resultKey, selectFirstOption]);

  const showClear = Boolean(allowClear && value && !disabled);

  return (
    <Combobox
      store={combobox}
      withinPortal
      width="target"
      position="bottom-start"
      onOptionSubmit={(employeeId) => {
        const employee = items.find((e) => e.id === employeeId);
        if (employee) onChange({ id: employee.id, displayName: employee.displayName });
        combobox.closeDropdown();
      }}
    >
      <Combobox.Target targetType="button" withExpandedAttribute>
        <InputBase
          id={id}
          component="button"
          type="button"
          pointer
          disabled={disabled}
          label={label}
          description={description}
          error={error}
          withAsterisk={withAsterisk}
          rightSection={
            showClear ? (
              <Combobox.ClearButton aria-label={t("employees:picker.clear")} title={t("employees:picker.clear")} onClear={() => onChange(null)} />
            ) : (
              <Combobox.Chevron />
            )
          }
          rightSectionPointerEvents={showClear ? "all" : "none"}
          onClick={() => combobox.toggleDropdown()}
        >
          <Text component="span" size="sm" truncate="end" c={value ? undefined : "dimmed"} className="block">
            {value?.displayName ?? placeholder ?? t("employees:picker.placeholder")}
          </Text>
        </InputBase>
      </Combobox.Target>

      <Combobox.Dropdown miw={288}>
        <Combobox.Search
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          placeholder={t("employees:picker.search")}
          leftSection={<Search size={14} />}
        />
        <Combobox.Options>
          <ScrollArea.Autosize mah={256} type="scroll">
            {isFetching && items.length === 0 ? (
              <Combobox.Empty>{t("employees:loading")}</Combobox.Empty>
            ) : items.length === 0 ? (
              <Combobox.Empty>{t("employees:picker.empty")}</Combobox.Empty>
            ) : (
              items.map((employee) => (
                <Combobox.Option key={employee.id} value={employee.id} active={value?.id === employee.id}>
                  <Group gap="xs" wrap="nowrap">
                    <Check size={16} style={{ opacity: value?.id === employee.id ? 1 : 0, flexShrink: 0 }} />
                    <div className="min-w-0">
                      <Text size="sm" truncate="end">
                        {employee.displayName}
                      </Text>
                      {employee.workEmail && (
                        <Text size="xs" c="dimmed" truncate="end">
                          {employee.workEmail}
                        </Text>
                      )}
                    </div>
                  </Group>
                </Combobox.Option>
              ))
            )}
          </ScrollArea.Autosize>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}
