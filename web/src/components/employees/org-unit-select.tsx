import { useMemo, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Check } from "lucide-react";
import { Group, Select } from "@mantine/core";
import { useOrgUnits } from "./use-org-units";

const EMPTY_VALUE = "__none__";

interface OrgUnitSelectProps {
  id?: string;
  value: string | null;
  onChange: (value: string | null) => void;
  /** Label of the "no unit" option (e.g. "Tüm birimler" for filters, "Birim yok" for forms). */
  emptyLabel: string;
  disabled?: boolean;
  className?: string;
  /** Optional field label/error rendered by the Mantine input wrapper. */
  label?: ReactNode;
  error?: ReactNode;
}

export default function OrgUnitSelect({ id, value, onChange, emptyLabel, disabled, className, label, error }: OrgUnitSelectProps) {
  const { t } = useTranslation(["employees"]);
  const { options, nameById } = useOrgUnits();

  const data = useMemo(
    () => [
      { value: EMPTY_VALUE, label: emptyLabel },
      ...(value && !nameById.has(value) ? [{ value, label: t("employees:unknownUnit") }] : []),
      ...options.map((unit) => ({ value: unit.id, label: unit.name })),
    ],
    [emptyLabel, value, nameById, options, t]
  );
  const depthById = useMemo(() => new Map(options.map((unit) => [unit.id, unit.depth])), [options]);

  return (
    <Select
      id={id}
      className={className}
      label={label}
      error={error}
      data={data}
      value={value ?? EMPTY_VALUE}
      onChange={(v) => onChange(v === null || v === EMPTY_VALUE ? null : v)}
      allowDeselect={false}
      disabled={disabled}
      renderOption={({ option, checked }) => (
        <Group gap="xs" wrap="nowrap">
          <Check size={14} style={{ opacity: checked ? 1 : 0, flexShrink: 0 }} />
          <span style={{ paddingLeft: `${(depthById.get(option.value) ?? 0) * 12}px` }}>{option.label}</span>
        </Group>
      )}
    />
  );
}
