import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, FileSpreadsheet, WandSparkles } from "lucide-react";
import { Button, Card, Checkbox, Group, Input, MultiSelect, Stack, Text, Title } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import { getCustomReportCatalog, requestReport, type CustomReportField } from "@/services/reports.service";

const DETAIL_PAIR = new Set(["leave.overtime|expense.payments", "expense.payments|leave.overtime"]);

function canCombineDetails(left: string, right: string) {
  return left === right || DETAIL_PAIR.has(`${left}|${right}`);
}

function defaultRange() {
  const to = new Date();
  const from = new Date(to.getFullYear(), to.getMonth() - 11, 1);
  return { from: toDateOnly(from), to: toDateOnly(to) };
}

function addMonths(dateValue: string, months: number) {
  const [year, month, day] = dateValue.split("-").map(Number);
  const date = new Date(year, month - 1 + months, day);
  if (date.getDate() !== day) date.setDate(0);
  return date;
}

function selectionHasValidRange(fields: CustomReportField[], selectedKeys: string[], from: string, to: string) {
  const details = fields.filter((field) => selectedKeys.includes(field.key) && field.grain === "detail");
  if (details.length === 0) return true;
  if (!from || !to || from > to) return false;
  const latest = addMonths(from, 12);
  latest.setDate(latest.getDate() - 1);
  return new Date(`${to}T00:00:00`) <= latest;
}

export function CustomReportBuilder() {
  const { t } = useTranslation(["reports", "common"]);
  const queryClient = useQueryClient();
  const { options: orgUnitOptions } = useOrgUnits();
  const [open, setOpen] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<string[]>([]);
  const [orgUnitIds, setOrgUnitIds] = useState<string[]>([]);
  const [range, setRange] = useState({ from: "", to: "" });

  const catalogQuery = useQuery({
    queryKey: ["reports", "custom", "catalog"],
    queryFn: getCustomReportCatalog,
    enabled: open,
    retry: false,
  });
  const fields = catalogQuery.data?.fields ?? [];
  const selectedDetailGroups = useMemo(
    () => [...new Set(fields.filter((field) => selectedKeys.includes(field.key) && field.grain === "detail").map((field) => field.groupKey))],
    [fields, selectedKeys],
  );
  const hasDetails = selectedDetailGroups.length > 0;
  const rangeValid = selectionHasValidRange(fields, selectedKeys, range.from, range.to);

  const mutation = useMutation({
    mutationFn: () =>
      requestReport("report.custom", {
        fieldKeys: JSON.stringify(selectedKeys),
        ...(orgUnitIds.length > 0 ? { orgUnitIds: orgUnitIds.join(",") } : {}),
        ...(hasDetails ? { from: range.from, to: range.to } : {}),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["reports", "executions"] });
      toast({ variant: "success", title: t("reports:build.queued") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("reports:build.error")) }),
  });

  const setSelection = (keys: string[]) => {
    setSelectedKeys(keys);
    const nextDetail = fields.find((field) => keys.includes(field.key) && field.grain === "detail");
    if (nextDetail && !range.from && !range.to) setRange(defaultRange());
  };

  return (
    <Card withBorder padding="lg" radius="md">
      <Group justify="space-between" align="center">
        <Group gap="sm">
          <FileSpreadsheet size={20} aria-hidden="true" />
          <div>
            <Title order={2} size="h4">{t("reports:build.title")}</Title>
            <Text size="sm" c="dimmed">{t("reports:build.subtitle")}</Text>
          </div>
        </Group>
        {!open && (
          <Button leftSection={<WandSparkles size={16} />} onClick={() => setOpen(true)}>
            {t("reports:build.open")}
          </Button>
        )}
      </Group>

      {open && (
        <Stack mt="lg" gap="md">
          {catalogQuery.isLoading ? <Text role="status">{t("reports:build.loading")}</Text> : null}
          {catalogQuery.isError ? <Text c="red">{t("reports:build.catalogError")}</Text> : null}
          {!catalogQuery.isLoading && !catalogQuery.isError && fields.length === 0 ? (
            <Text c="dimmed">{t("reports:build.empty")}</Text>
          ) : null}

          {catalogQuery.data?.groups.map((group) => {
            const groupFields = fields.filter((field) => field.groupKey === group.key);
            if (groupFields.length === 0) return null;
            return (
              <Card key={group.key} withBorder padding="md" radius="sm">
                <Checkbox.Group value={selectedKeys} onChange={setSelection}>
                  <Stack gap="xs">
                    <Text fw={600}>{t(group.labelKey)}</Text>
                    {groupFields.map((field) => {
                      const existing = selectedDetailGroups[0];
                      const disabled = field.grain === "detail" && existing !== undefined && !canCombineDetails(existing, field.groupKey);
                      return <Checkbox key={field.key} value={field.key} label={t(field.labelKey)} disabled={disabled} />;
                    })}
                  </Stack>
                </Checkbox.Group>
              </Card>
            );
          })}

          <MultiSelect
            label={t("reports:params.orgUnitIds")}
            description={t("reports:params.orgUnitIdsHint")}
            placeholder={t("reports:params.orgUnitIdsPlaceholder")}
            data={orgUnitOptions.map((unit) => ({ value: unit.id, label: unit.name }))}
            value={orgUnitIds}
            onChange={setOrgUnitIds}
            searchable
            clearable
            renderOption={({ option, checked }) => (
              <Group gap="xs" wrap="nowrap">
                <Check size={14} style={{ opacity: checked ? 1 : 0, flexShrink: 0 }} />
                <span>{option.label}</span>
              </Group>
            )}
          />

          {hasDetails ? (
            <Stack gap="xs">
              <Text size="sm" fw={500}>{t("reports:params.dateRange")}</Text>
              <Group grow align="flex-end">
                <Input.Wrapper id="custom-report-from" label={t("reports:params.from")}>
                  <DatePicker id="custom-report-from" value={range.from} max={range.to || undefined} onChange={(from) => setRange((current) => ({ ...current, from }))} />
                </Input.Wrapper>
                <Input.Wrapper id="custom-report-to" label={t("reports:params.to")}>
                  <DatePicker id="custom-report-to" value={range.to} min={range.from || undefined} onChange={(to) => setRange((current) => ({ ...current, to }))} />
                </Input.Wrapper>
              </Group>
              {!rangeValid ? <Text size="xs" c="red">{t("reports:build.invalidRange")}</Text> : null}
            </Stack>
          ) : null}

          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOpen(false)}>{t("common:cancel")}</Button>
            <Button
              leftSection={<FileSpreadsheet size={16} />}
              loading={mutation.isPending}
              disabled={selectedKeys.length === 0 || !rangeValid || catalogQuery.isError || fields.length === 0}
              onClick={() => mutation.mutate()}
            >
              {t("reports:build.generate")}
            </Button>
          </Group>
        </Stack>
      )}
    </Card>
  );
}
