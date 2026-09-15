import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { CloseButton, Combobox, Group, Input, InputBase, Loader, ScrollArea, Text, useCombobox } from "@mantine/core";

export interface PickerOption {
  id: string;
  label: string;
  description?: string | null;
}

interface SearchPickerProps {
  id?: string;
  /** react-query key prefix; the debounced search text is appended. */
  queryKey: readonly unknown[];
  search: (q: string) => Promise<PickerOption[]>;
  value: PickerOption | null;
  onChange: (option: PickerOption | null) => void;
  placeholder: string;
  allowClear?: boolean;
  disabled?: boolean;
  /** Called when the search request fails (e.g. 403) so callers can hide the field. */
  onError?: (error: unknown) => void;
  /** Field label rendered by the Mantine input wrapper. */
  label?: ReactNode;
  /** Validation message rendered under the field. */
  error?: ReactNode;
}

/** Async search combobox (dropdown + server-side search, keyboard navigable) used for person pickers. */
export default function SearchPicker({
  id,
  queryKey,
  search,
  value,
  onChange,
  placeholder,
  allowClear = true,
  disabled,
  onError,
  label,
  error: fieldError,
}: SearchPickerProps) {
  const { t } = useTranslation(["structure", "common"]);
  const [text, setText] = useState("");
  const [debounced, setDebounced] = useState("");

  const combobox = useCombobox({
    onDropdownOpen: () => combobox.focusSearchInput(),
    onDropdownClose: () => combobox.resetSelectedOption(),
  });
  const open = combobox.dropdownOpened;
  const { selectFirstOption } = combobox;

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(text.trim()), 300);
    return () => clearTimeout(timer);
  }, [text]);

  const { data, isFetching, error } = useQuery({
    queryKey: [...queryKey, debounced],
    queryFn: () => search(debounced),
    enabled: open,
    retry: false,
  });

  useEffect(() => {
    if (error && onError) onError(error);
  }, [error, onError]);

  // Highlight the first result so Enter picks it, like the previous command list did.
  useEffect(() => {
    if (open && data && data.length > 0) selectFirstOption();
  }, [open, data, selectFirstOption]);

  const options = data ?? [];
  const showClear = allowClear && !!value && !disabled;

  return (
    <Combobox
      store={combobox}
      disabled={disabled}
      onOptionSubmit={(optionId) => {
        const option = options.find((o) => o.id === optionId);
        if (option) onChange(option);
        combobox.closeDropdown();
      }}
    >
      <Combobox.Target targetType="button">
        <InputBase
          id={id}
          component="button"
          type="button"
          pointer
          label={label}
          error={fieldError}
          disabled={disabled}
          aria-expanded={open}
          onClick={() => combobox.toggleDropdown()}
          rightSection={
            showClear ? (
              <CloseButton
                size="sm"
                title={t("structure:clearSelection")}
                aria-label={t("structure:clearSelection")}
                onMouseDown={(e) => e.preventDefault()}
                onClick={(e) => {
                  e.stopPropagation();
                  onChange(null);
                }}
              />
            ) : (
              <Combobox.Chevron />
            )
          }
          rightSectionPointerEvents={showClear ? "all" : "none"}
        >
          {value ? (
            <Text span size="sm" truncate="end" display="block">
              {value.label}
            </Text>
          ) : (
            <Input.Placeholder>{placeholder}</Input.Placeholder>
          )}
        </InputBase>
      </Combobox.Target>

      <Combobox.Dropdown miw={288}>
        <Combobox.Search
          value={text}
          onChange={(e) => setText(e.currentTarget.value)}
          placeholder={t("structure:searchPerson")}
        />
        <Combobox.Options>
          <ScrollArea.Autosize mah={240} type="scroll">
            {isFetching && !data ? (
              <Combobox.Empty>
                <Group justify="center">
                  <Loader size="xs" />
                </Group>
              </Combobox.Empty>
            ) : error ? (
              <Combobox.Empty>{t("common:errorGeneric")}</Combobox.Empty>
            ) : options.length === 0 ? (
              <Combobox.Empty>{t("common:noResults")}</Combobox.Empty>
            ) : (
              options.map((option) => {
                const selected = value?.id === option.id;
                return (
                  <Combobox.Option key={option.id} value={option.id} active={selected}>
                    <Group gap="xs" wrap="nowrap">
                      <Check size={16} style={{ flexShrink: 0, opacity: selected ? 1 : 0 }} />
                      <div style={{ minWidth: 0 }}>
                        <Text size="sm" truncate="end">
                          {option.label}
                        </Text>
                        {option.description && (
                          <Text size="xs" c="dimmed" truncate="end">
                            {option.description}
                          </Text>
                        )}
                      </div>
                    </Group>
                  </Combobox.Option>
                );
              })
            )}
          </ScrollArea.Autosize>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}
