import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Badge, Button, Checkbox, Group, Modal, Select, Skeleton, Stack, Table, Text, TextInput } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listPayrollPeriods } from "@/services/payroll.service";
import {
  convertLegacyProfileCode,
  listIncentiveDefinitions,
  listLegacyProfileCodes,
  type IncentiveDefinition,
  type LegacyProfileCode,
} from "@/services/payroll-incentives.service";
import { DefinitionAdvice, INCENTIVE_LEGACY_QUERY_KEY, useRefreshIncentiveAssignments } from "./incentive-assign-modal";
import { parseMonthValue, toMonthValue } from "./pay-item-assign-modal";

/** True when an assignment of this definition would not reproduce today's flat 5 points (mirrors the server's `calculationChanges`). */
export function definitionChangesCalculation(definition: IncentiveDefinition): boolean {
  return !(definition.isSmmmApproved === true && definition.kind === "employerSgkRateDiscount");
}

/** First open payroll period as "YYYY-MM", or the current month when none is open. */
export function suggestStartMonth(periods: { year: number; month: number; status: string }[], today: Date): string {
  const open = periods.filter((period) => period.status === "open").sort((a, b) => a.year * 12 + a.month - (b.year * 12 + b.month));
  return open.length > 0 ? toMonthValue(open[0].year, open[0].month) : toMonthValue(today.getFullYear(), today.getMonth() + 1);
}

/** "Eski profil kodları" tab: the preview of the profile law codes in use and the per-code conversion into assignments. */
export function IncentiveLegacyConvert() {
  const { t } = useTranslation(["payroll"]);
  const [converting, setConverting] = useState<LegacyProfileCode | null>(null);
  const query = useQuery({ queryKey: INCENTIVE_LEGACY_QUERY_KEY, queryFn: () => listLegacyProfileCodes() });
  const rows = query.data ?? [];
  const sharedNormalized = new Set(
    rows.filter((row) => rows.some((other) => other !== row && other.normalizedCode === row.normalizedCode)).map((row) => row.normalizedCode),
  );

  return (
    <Stack gap="sm">
      <Text size="sm" c="dimmed">
        {t("payroll:incentiveLegacy.intro")}
      </Text>
      <Alert color="blue" data-testid="legacy-grouping-note">
        {t("payroll:incentiveLegacy.groupingNote")}
      </Alert>
      {query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("payroll:incentiveLegacy.loadError"))}
        </Text>
      ) : query.isLoading ? (
        <Skeleton h={96} />
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed" ta="center" p="lg" data-testid="legacy-empty">
          {t("payroll:incentiveLegacy.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={760}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:incentiveLegacy.columns.code")}</Table.Th>
                <Table.Th>{t("payroll:incentiveLegacy.columns.normalizedCode")}</Table.Th>
                <Table.Th ta="right">{t("payroll:incentiveLegacy.columns.employeeCount")}</Table.Th>
                <Table.Th>{t("payroll:incentiveLegacy.columns.suggested")}</Table.Th>
                <Table.Th>{t("payroll:incentiveLegacy.columns.effect")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => (
                <Table.Tr key={row.code} data-testid={`legacy-row-${row.code}`}>
                  <Table.Td>
                    <Text ff="monospace" size="sm">
                      {row.code}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text ff="monospace" size="sm">
                      {row.normalizedCode}
                    </Text>
                    {sharedNormalized.has(row.normalizedCode) && (
                      <Text size="xs" c="dimmed">
                        {t("payroll:incentiveLegacy.sharedNormalized", { code: row.normalizedCode })}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td ta="right">{row.employeeCount}</Table.Td>
                  <Table.Td>
                    {row.suggestedDefinitionId ? (
                      <>
                        <Text size="sm">{row.suggestedDefinitionCode}</Text>
                        <Badge color={row.suggestedIsApproved ? "green" : "yellow"} variant="light">
                          {row.suggestedIsApproved ? t("payroll:incentives.approval.approved") : t("payroll:incentives.approval.pending")}
                        </Badge>
                      </>
                    ) : (
                      <Text size="sm" c="dimmed">
                        {t("payroll:incentiveLegacy.noSuggestion")}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {row.calculationChanges ? (
                      <Text size="sm" c="red" fw={600}>
                        {t("payroll:incentiveLegacy.calculationChanges")}
                      </Text>
                    ) : (
                      <Text size="sm" c="dimmed">
                        {t("payroll:incentiveLegacy.calculationSame")}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Button size="compact-xs" variant="light" onClick={() => setConverting(row)}>
                      {t("payroll:incentiveLegacy.convert")}
                    </Button>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Modal opened={converting !== null} onClose={() => setConverting(null)} size="36rem" title={t("payroll:incentiveLegacy.modal.title", { code: converting?.code ?? "" })}>
        {converting && <ConvertBody key={converting.code} row={converting} onClose={() => setConverting(null)} />}
      </Modal>
    </Stack>
  );
}

function ConvertBody({ row, onClose }: { row: LegacyProfileCode; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshIncentiveAssignments();
  const [definitionId, setDefinitionId] = useState<string | null>(row.suggestedDefinitionId ?? null);
  const [fromMonth, setFromMonth] = useState<string | null>(null);
  const [toMonth, setToMonth] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const definitions = useQuery({ queryKey: ["payroll", "incentive-definitions", "active"], queryFn: () => listIncentiveDefinitions(false) });
  const periods = useQuery({ queryKey: ["payroll", "periods"], queryFn: listPayrollPeriods, retry: false });
  const options = (definitions.data ?? []).filter((item) => item.isActive !== false && item.kind !== "incomeTaxWithholdingRelief");
  const selected = options.find((item) => item.id === definitionId);

  // The start month defaults to the first OPEN period (earlier months are closed and refused by the server).
  const suggestedStart = periods.isLoading ? null : suggestStartMonth(periods.data ?? [], new Date());
  const startValue = fromMonth ?? suggestedStart ?? "";
  const from = parseMonthValue(startValue);
  const to = parseMonthValue(toMonth);
  const toInvalid = toMonth !== "" && to === null;
  const endRequired = selected?.maxMonths != null && to === null;

  const changes = selected !== undefined && (definitionChangesCalculation(selected) || (selected.id === row.suggestedDefinitionId && row.calculationChanges === true));
  const canSubmit = selected !== undefined && from !== null && !toInvalid && !endRequired && (!changes || confirmed);

  const save = useMutation({
    mutationFn: () =>
      convertLegacyProfileCode({
        code: row.code,
        incentiveDefinitionId: selected!.id,
        fromYear: from!.year,
        fromMonth: from!.month,
        toYear: to?.year ?? null,
        toMonth: to?.month ?? null,
      }),
    onSuccess: async (result) => {
      await refresh();
      toast({
        title: t("payroll:incentiveLegacy.result.title"),
        description: t("payroll:incentiveLegacy.result.summary", { created: result.createdIds.length, skipped: result.skippedEmployeeIds.length }),
      });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentiveLegacy.modal.error"))),
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        save.mutate();
      }}
    >
      <Stack gap="sm">
        <Text size="sm">{t("payroll:incentiveLegacy.modal.scope", { count: row.employeeCount, code: row.normalizedCode })}</Text>
        <Alert color="blue">{t("payroll:incentiveLegacy.modal.profileUntouched")}</Alert>
        <Select
          label={t("payroll:incentiveLegacy.modal.definition")}
          placeholder={t("payroll:incentiveLegacy.modal.definitionPlaceholder")}
          nothingFoundMessage={t("payroll:incentiveLegacy.modal.noDefinitions")}
          data={options.map((item) => ({ value: item.id, label: `${item.code} - ${item.name}` }))}
          value={definitionId}
          onChange={(value) => {
            setDefinitionId(value);
            setConfirmed(false);
          }}
          searchable
          required
        />
        <DefinitionAdvice definition={selected} fromMonth={startValue} />
        {changes && (
          <Alert color="red" title={t("payroll:incentiveLegacy.modal.dangerTitle")} data-testid="legacy-calculation-warning">
            <Stack gap="xs">
              <Text size="sm">{t("payroll:incentiveLegacy.modal.dangerText")}</Text>
              <Checkbox
                checked={confirmed}
                onChange={(event) => setConfirmed(event.currentTarget.checked)}
                label={t("payroll:incentiveLegacy.modal.confirm")}
              />
            </Stack>
          </Alert>
        )}
        <Group grow align="flex-start">
          <TextInput
            type="month"
            label={t("payroll:incentiveLegacy.modal.fromMonth")}
            description={t("payroll:incentiveLegacy.modal.fromMonthHint")}
            value={startValue}
            onChange={(event) => setFromMonth(event.currentTarget.value)}
            required
          />
          <TextInput
            type="month"
            label={t("payroll:incentiveLegacy.modal.toMonth")}
            description={
              selected?.maxMonths != null
                ? t("payroll:incentiveAssignments.modal.toMonthRequired", { count: selected.maxMonths })
                : t("payroll:incentiveAssignments.modal.toMonthHint")
            }
            value={toMonth}
            onChange={(event) => setToMonth(event.currentTarget.value)}
            required={selected?.maxMonths != null}
          />
        </Group>
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={!canSubmit}>
            {t("payroll:incentiveLegacy.modal.submit")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
