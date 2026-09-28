import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Check } from "lucide-react";
import { Button, Group, Input, MultiSelect, Select, Stack, Switch, Text } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { toDateOnly } from "@/lib/dates";
import { REPORT_PARAMETER_KEYS, type ReportCatalogItem } from "@/services/reports.service";
import type { ParameterValues } from "./report-parameters";

interface ReportParameterFormProps {
  report: ReportCatalogItem;
  values: ParameterValues;
  onChange: (values: ParameterValues) => void;
}

/** Kolay İK's "Birimler / Ay / Yıl / tarih aralığı" step, drawn from the report's parameter descriptors (controlled). */
export function ReportParameterForm({ report, values, onChange }: ReportParameterFormProps) {
  const { t, i18n } = useTranslation(["reports"]);
  const today = useMemo(() => new Date(), []);
  const { options: orgUnitOptions } = useOrgUnits();
  const hasRequired = report.parameters.some((p) => p.isRequired);

  const set = (key: string, value: string | null | undefined) => {
    const next = { ...values };
    if (value === null || value === undefined || value === "") delete next[key];
    else next[key] = value;
    onChange(next);
  };

  const monthOptions = useMemo(
    () =>
      Array.from({ length: 12 }, (_, i) => ({
        value: String(i + 1),
        label: new Intl.DateTimeFormat(i18n.language, { month: "long" }).format(new Date(2000, i, 1)),
      })),
    [i18n.language],
  );
  const yearOptions = useMemo(() => Array.from({ length: 7 }, (_, i) => String(today.getFullYear() + 1 - i)), [today]);
  const depthById = useMemo(() => new Map(orgUnitOptions.map((u) => [u.id, u.depth])), [orgUnitOptions]);

  const applyPreset = (preset: "thisMonth" | "lastMonth" | "thisYear") => {
    const y = today.getFullYear();
    const m = today.getMonth();
    const [from, to] =
      preset === "thisMonth"
        ? [new Date(y, m, 1), new Date(y, m + 1, 0)]
        : preset === "lastMonth"
          ? [new Date(y, m - 1, 1), new Date(y, m, 0)]
          : [new Date(y, 0, 1), new Date(y, 11, 31)];
    onChange({ ...values, [REPORT_PARAMETER_KEYS.from]: toDateOnly(from), [REPORT_PARAMETER_KEYS.to]: toDateOnly(to) });
  };

  if (report.parameters.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t("reports:run.noFilters")}
      </Text>
    );
  }

  return (
    <Stack gap="sm">
      {report.parameters.map((parameter) => {
        const required = parameter.isRequired ? " *" : "";
        switch (parameter.kind) {
          case "orgUnits":
            return (
              <MultiSelect
                key={parameter.key}
                label={t("reports:params.orgUnitIds") + required}
                description={t("reports:params.orgUnitIdsHint")}
                placeholder={t("reports:params.orgUnitIdsPlaceholder")}
                data={orgUnitOptions.map((u) => ({ value: u.id, label: u.name }))}
                value={values[parameter.key] ? values[parameter.key].split(",") : []}
                onChange={(ids) => set(parameter.key, ids.join(","))}
                searchable
                clearable
                renderOption={({ option, checked }) => (
                  <Group gap="xs" wrap="nowrap">
                    <Check size={14} style={{ opacity: checked ? 1 : 0, flexShrink: 0 }} />
                    <span style={{ paddingLeft: `${(depthById.get(option.value) ?? 0) * 12}px` }}>{option.label}</span>
                  </Group>
                )}
              />
            );
          case "year":
            return (
              <Select
                key={parameter.key}
                label={t("reports:params.year") + required}
                data={yearOptions}
                value={values[parameter.key] ?? null}
                onChange={(v) => set(parameter.key, v)}
                clearable={!parameter.isRequired}
              />
            );
          case "month":
            return (
              <Select
                key={parameter.key}
                label={t("reports:params.month") + required}
                placeholder={t("reports:params.monthPlaceholder")}
                data={monthOptions}
                value={values[parameter.key] ?? null}
                onChange={(v) => set(parameter.key, v)}
                clearable={!parameter.isRequired}
              />
            );
          case "dateRange":
            return (
              <Stack key={parameter.key} gap={6}>
                <Text size="sm" fw={500}>
                  {t("reports:params.dateRange") + required}
                </Text>
                <Group grow align="flex-end" gap="sm">
                  <Input.Wrapper id={`${report.key}-from`} label={t("reports:params.from")}>
                    <DatePicker
                      id={`${report.key}-from`}
                      value={values[REPORT_PARAMETER_KEYS.from] ?? ""}
                      max={values[REPORT_PARAMETER_KEYS.to] || undefined}
                      clearable={!parameter.isRequired}
                      onChange={(v) => set(REPORT_PARAMETER_KEYS.from, v)}
                    />
                  </Input.Wrapper>
                  <Input.Wrapper id={`${report.key}-to`} label={t("reports:params.to")}>
                    <DatePicker
                      id={`${report.key}-to`}
                      value={values[REPORT_PARAMETER_KEYS.to] ?? ""}
                      min={values[REPORT_PARAMETER_KEYS.from] || undefined}
                      clearable={!parameter.isRequired}
                      onChange={(v) => set(REPORT_PARAMETER_KEYS.to, v)}
                    />
                  </Input.Wrapper>
                </Group>
                <Group gap={6}>
                  {(["thisMonth", "lastMonth", "thisYear"] as const).map((preset) => (
                    <Button key={preset} type="button" size="compact-xs" variant="light" color="gray" onClick={() => applyPreset(preset)}>
                      {t(`reports:params.presets.${preset}`)}
                    </Button>
                  ))}
                </Group>
              </Stack>
            );
          case "boolean":
            return (
              <Switch
                key={parameter.key}
                label={t(`reports:params.${parameter.key}`, { defaultValue: parameter.key })}
                checked={values[parameter.key] === "true"}
                onChange={(e) => set(parameter.key, e.currentTarget.checked ? "true" : null)}
              />
            );
          default:
            return null;
        }
      })}
      {hasRequired && (
        <Text size="xs" c="dimmed">
          {t("reports:run.requiredHint")}
        </Text>
      )}
    </Stack>
  );
}
