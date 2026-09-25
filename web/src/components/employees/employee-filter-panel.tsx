import { useTranslation } from "react-i18next";
import { Group, MultiSelect, Select, Switch, TextInput } from "@mantine/core";
import { Search } from "lucide-react";
import { EMPLOYEE_STATUSES } from "@/services/employees.service";
import type { EmployeeFilter } from "@/types";
import { useOrgUnits } from "./use-org-units";

interface EmployeeFilterPanelProps {
  value: EmployeeFilter;
  onChange: (value: EmployeeFilter) => void;
}

/** Controlled filter form for employee pickers: search, org units (any type), status, terminated toggle. */
export default function EmployeeFilterPanel({ value, onChange }: EmployeeFilterPanelProps) {
  const { t } = useTranslation(["employees"]);
  const { options } = useOrgUnits();

  return (
    <Group align="flex-end" gap="sm">
      <TextInput
        label={t("employees:multiPicker.search")}
        leftSection={<Search size={14} />}
        value={value.q ?? ""}
        onChange={(e) => onChange({ ...value, q: e.currentTarget.value || null })}
      />
      <MultiSelect
        label={t("employees:multiPicker.orgUnits")}
        data={options.map((unit) => ({ value: unit.id, label: unit.name }))}
        value={value.orgUnitIds ?? []}
        onChange={(ids) => onChange({ ...value, orgUnitIds: ids.length > 0 ? ids : null })}
        searchable
        clearable
        miw={200}
      />
      <Select
        label={t("employees:multiPicker.status")}
        placeholder={t("employees:multiPicker.allStatuses")}
        data={EMPLOYEE_STATUSES.map((status) => ({ value: status, label: t(`employees:status.${status}`) }))}
        value={value.status ?? null}
        onChange={(status) => onChange({ ...value, status })}
        clearable
      />
      <Switch
        label={t("employees:multiPicker.includeTerminated")}
        checked={value.includeTerminated === true}
        onChange={(e) => onChange({ ...value, includeTerminated: e.currentTarget.checked ? true : null })}
        pb={6}
      />
    </Group>
  );
}
