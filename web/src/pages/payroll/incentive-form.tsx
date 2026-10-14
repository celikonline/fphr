import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Checkbox, Group, Modal, NumberInput, SegmentedControl, Select, Stack, Text, TextInput } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createIncentiveDefinition,
  getIncentiveParameterCatalog,
  listIncentiveTemplates,
  updateIncentiveDefinition,
  type IncentiveCoverageBase,
  type IncentiveDefinition,
  type IncentiveDefinitionInput,
  type IncentiveKind,
  type IncentiveParameters,
  type IncentiveRate,
  type LegislationKeyValue,
} from "@/services/payroll-incentives.service";

export const INCENTIVES_QUERY_KEY = ["payroll", "incentive-definitions"] as const;
const CATALOG_STALE_MS = 5 * 60 * 1000;
const KINDS: IncentiveKind[] = ["employerSgkRateDiscount", "employerShareCoverage", "incomeTaxWithholdingRelief", "declarationOnly"];
const COVERAGE_BASES: IncentiveCoverageBase[] = ["actualSgkBase", "minimumWage"];
const DECLARATION_CODE_PATTERN = /^\d{1,10}$/;

/** A rate in the form: a literal percent (0..100) or a legislation parameter key. */
interface RateState {
  mode: "percent" | "key";
  percent: number | string;
  key: string;
}

interface FormState {
  code: string;
  name: string;
  lawReference: string;
  declarationCode: string;
  kind: IncentiveKind;
  rate: RateState;
  coverageBase: IncentiveCoverageBase;
  capMultiplier: number | string;
  maxSupportMultiplier: number | string;
  taxReliefGrossCapMultiplier: number | string;
  employerSgkRate: RateState;
  coversEmployerSgk: boolean;
  coversEmployerUnemployment: boolean;
  coversEmployeeSgk: boolean;
  coversEmployeeUnemployment: boolean;
  maxMonths: number | string;
  validFrom: string;
  validTo: string;
}

const EMPTY_RATE: RateState = { mode: "percent", percent: "", key: "" };

function rateFromDto(rate: IncentiveRate | null | undefined): RateState {
  if (rate?.legislationKey) return { mode: "key", percent: "", key: rate.legislationKey };
  if (rate?.value !== null && rate?.value !== undefined) return { mode: "percent", percent: Number((rate.value * 100).toFixed(4)), key: "" };
  return EMPTY_RATE;
}

function rateToDto(rate: RateState): IncentiveRate | null {
  if (rate.mode === "key") return rate.key ? { legislationKey: rate.key } : null;
  return rate.percent === "" ? null : { value: Number((Number(rate.percent) / 100).toFixed(6)) };
}

function stateFromDefinition(item: IncentiveDefinition | null): FormState {
  const p = item?.parameters;
  return {
    code: item?.code ?? "",
    name: item?.name ?? "",
    lawReference: item?.lawReference ?? "",
    declarationCode: item?.declarationCode ?? "",
    kind: item?.kind ?? "employerSgkRateDiscount",
    rate: rateFromDto(p?.rate),
    coverageBase: p?.coverageBase ?? "actualSgkBase",
    capMultiplier: p?.capMultiplier ?? "",
    maxSupportMultiplier: p?.maxSupportMultiplier ?? "",
    taxReliefGrossCapMultiplier: p?.taxReliefGrossCapMultiplier ?? "",
    employerSgkRate: rateFromDto(p?.employerSgkRate),
    coversEmployerSgk: p?.coversEmployerSgk ?? false,
    coversEmployerUnemployment: p?.coversEmployerUnemployment ?? false,
    coversEmployeeSgk: p?.coversEmployeeSgk ?? false,
    coversEmployeeUnemployment: p?.coversEmployeeUnemployment ?? false,
    maxMonths: item?.maxMonths ?? "",
    validFrom: item?.validFrom ?? "",
    validTo: item?.validTo ?? "",
  };
}

/** Only the parameters the chosen kind uses are sent (the domain rejects the rest). Drafts may be incomplete. */
function parametersOf(s: FormState): IncentiveParameters | null {
  switch (s.kind) {
    case "declarationOnly":
      return null;
    case "employerShareCoverage": {
      const minimumWage = s.coverageBase === "minimumWage";
      return {
        rate: rateToDto(s.rate),
        coverageBase: s.coverageBase,
        capMultiplier: minimumWage && s.capMultiplier !== "" ? Number(s.capMultiplier) : null,
        ...(s.maxSupportMultiplier === "" ? {} : { maxSupportMultiplier: Number(s.maxSupportMultiplier) }),
        employerSgkRate: s.coversEmployerSgk ? rateToDto(s.employerSgkRate) : null,
        coversEmployerSgk: s.coversEmployerSgk,
        coversEmployerUnemployment: s.coversEmployerUnemployment,
        coversEmployeeSgk: s.coversEmployeeSgk,
        coversEmployeeUnemployment: s.coversEmployeeUnemployment,
      };
    }
    case "incomeTaxWithholdingRelief":
      return { rate: rateToDto(s.rate), ...(s.taxReliefGrossCapMultiplier === "" ? {} : { taxReliefGrossCapMultiplier: Number(s.taxReliefGrossCapMultiplier) }) };
    default:
      return { rate: rateToDto(s.rate) };
  }
}

function toInput(s: FormState): IncentiveDefinitionInput {
  return {
    name: s.name.trim(),
    lawReference: s.lawReference.trim() || null,
    declarationCode: s.declarationCode.trim() || null,
    kind: s.kind,
    parameters: parametersOf(s),
    maxMonths: s.maxMonths === "" ? null : Number(s.maxMonths),
    validFrom: s.validFrom || null,
    validTo: s.validTo || null,
  };
}

/** The fields whose change drops the SMMM approval (kind, parameters, declaration code, max months, dates). */
function calculationSignature(input: IncentiveDefinitionInput): string {
  return JSON.stringify([input.kind, input.parameters ?? null, input.declarationCode ?? null, input.maxMonths ?? null, input.validFrom ?? null, input.validTo ?? null]);
}

interface IncentiveFormProps {
  opened: boolean;
  onClose: () => void;
  /** Present when editing; absent when creating. */
  item?: IncentiveDefinition | null;
}

/** Create/edit modal. The code is chosen once and locked; fields open per kind; an incomplete draft can be saved (completeness is checked on approval). */
export function IncentiveForm({ opened, onClose, item }: IncentiveFormProps) {
  // Remount per open/item so the local form state always starts from the right values.
  return (
    <Modal opened={opened} onClose={onClose} size="46rem" title={<FormTitle editing={Boolean(item)} />}>
      {opened && <IncentiveFormBody key={item?.id ?? "new"} item={item ?? null} onClose={onClose} />}
    </Modal>
  );
}

function FormTitle({ editing }: { editing: boolean }) {
  const { t } = useTranslation(["payroll"]);
  return <>{editing ? t("payroll:incentives.form.editTitle") : t("payroll:incentives.form.createTitle")}</>;
}

function IncentiveFormBody({ item, onClose }: { item: IncentiveDefinition | null; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const editing = item !== null;
  const [state, setState] = useState<FormState>(() => stateFromDefinition(item));
  const [error, setError] = useState<string | null>(null);
  const patch = (changes: Partial<FormState>) => setState((current) => ({ ...current, ...changes }));

  const catalogQuery = useQuery({ queryKey: [...INCENTIVES_QUERY_KEY, "parameter-catalog"], queryFn: getIncentiveParameterCatalog, staleTime: CATALOG_STALE_MS });
  const catalog = catalogQuery.data?.legislationKeys ?? [];
  const templatesQuery = useQuery({
    queryKey: [...INCENTIVES_QUERY_KEY, "templates"],
    queryFn: () => listIncentiveTemplates(),
    enabled: Boolean(item?.templateKey),
  });
  const originalTemplate = templatesQuery.data?.find((template) => template.templateKey === item?.templateKey && template.parameters?.rate != null);

  const input = toInput(state);
  const dropsApproval = Boolean(item?.isSmmmApproved) && calculationSignature(input) !== calculationSignature(toInput(stateFromDefinition(item)));
  const declarationCodeInvalid = state.declarationCode.trim() !== "" && !DECLARATION_CODE_PATTERN.test(state.declarationCode.trim());
  const canSubmit = (editing || state.code.trim().length > 0) && state.name.trim().length > 0 && !declarationCodeInvalid;

  const save = useMutation({
    mutationFn: () => (item ? updateIncentiveDefinition(item.id, input) : createIncentiveDefinition({ ...input, code: state.code.trim() }).then(() => undefined)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: INCENTIVES_QUERY_KEY });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentives.form.error"))),
  });

  const kind = state.kind;
  const usesRate = kind !== "declarationOnly";
  const isCoverage = kind === "employerShareCoverage";

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        save.mutate();
      }}
    >
      <Stack gap="sm">
        {originalTemplate && (
          <Button
            type="button"
            variant="light"
            onClick={() => {
              const model = stateFromDefinition({
                ...item!,
                kind: originalTemplate.kind,
                declarationCode: originalTemplate.declarationCode,
                parameters: originalTemplate.parameters,
                maxMonths: originalTemplate.maxMonths,
                validFrom: originalTemplate.validFrom ?? state.validFrom,
                validTo: originalTemplate.validTo,
              });
              patch({ ...model, code: state.code, name: state.name, lawReference: state.lawReference });
            }}
          >
            {t("payroll:incentives.form.applyTemplate")}
          </Button>
        )}
        <TextInput
          label={t("payroll:incentives.form.code")}
          description={editing ? t("payroll:incentives.form.codeLocked") : t("payroll:incentives.form.codeHint")}
          value={state.code}
          onChange={(event) => patch({ code: event.currentTarget.value.toUpperCase() })}
          disabled={editing}
          maxLength={32}
          required
        />
        <TextInput label={t("payroll:incentives.form.name")} value={state.name} onChange={(event) => patch({ name: event.currentTarget.value })} maxLength={100} required />
        <TextInput
          label={t("payroll:incentives.form.lawReference")}
          value={state.lawReference}
          onChange={(event) => patch({ lawReference: event.currentTarget.value })}
          maxLength={200}
        />
        <TextInput
          label={t("payroll:incentives.form.declarationCode")}
          description={t("payroll:incentives.form.declarationCodeHint")}
          value={state.declarationCode}
          onChange={(event) => patch({ declarationCode: event.currentTarget.value })}
          error={declarationCodeInvalid ? t("payroll:incentives.form.declarationCodeInvalid") : undefined}
          maxLength={10}
        />

        <Select
          label={t("payroll:incentives.form.kind")}
          description={editing ? t("payroll:incentives.form.kindLockHint") : undefined}
          data={KINDS.map((value) => ({ value, label: t(`payroll:incentives.kind.${value}`) }))}
          value={kind}
          onChange={(value) => value && patch({ kind: value as IncentiveKind })}
          allowDeselect={false}
        />
        <Text size="xs" c="dimmed" data-testid="incentive-kind-hint">
          {t(`payroll:incentives.kindHint.${kind}`)}
        </Text>

        {usesRate && (
          <RateField
            label={t(`payroll:incentives.form.rateLabel.${kind}`)}
            value={state.rate}
            onChange={(rate) => patch({ rate })}
            catalog={catalog}
            catalogFailed={catalogQuery.isError}
          />
        )}

        {kind === "incomeTaxWithholdingRelief" && (
          <NumberInput
            label={t("payroll:incentives.form.taxReliefGrossCapMultiplier")}
            description={t("payroll:incentives.form.taxReliefGrossCapMultiplierHint")}
            value={state.taxReliefGrossCapMultiplier}
            onChange={(taxReliefGrossCapMultiplier) => patch({ taxReliefGrossCapMultiplier })}
            min={0.01} max={100} decimalScale={2}
          />
        )}

        {isCoverage && (
          <>
            <Select
              label={t("payroll:incentives.form.coverageBase")}
              data={COVERAGE_BASES.map((value) => ({ value, label: t(`payroll:incentives.coverageBase.${value}`) }))}
              value={state.coverageBase}
              onChange={(value) => value && patch({ coverageBase: value as IncentiveCoverageBase })}
              allowDeselect={false}
            />
            {state.coverageBase === "minimumWage" && (
              <NumberInput
                label={t("payroll:incentives.form.capMultiplier")}
                description={t("payroll:incentives.form.capMultiplierHint")}
                value={state.capMultiplier}
                onChange={(capMultiplier) => patch({ capMultiplier })}
                decimalScale={4}
                min={0}
                max={10}
              />
            )}
            <NumberInput
              label={t("payroll:incentives.form.maxSupportMultiplier")}
              description={t("payroll:incentives.form.maxSupportMultiplierHint")}
              value={state.maxSupportMultiplier}
              onChange={(maxSupportMultiplier) => patch({ maxSupportMultiplier })}
              decimalScale={4}
              min={0}
              max={10}
            />
            <div>
              <Text size="sm" fw={500} mb={4}>
                {t("payroll:incentives.form.covers")}
              </Text>
              <Group gap="lg">
                <Checkbox
                  label={t("payroll:incentives.form.coversEmployerSgk")}
                  checked={state.coversEmployerSgk}
                  onChange={(event) => patch({ coversEmployerSgk: event.currentTarget.checked })}
                />
                <Checkbox
                  label={t("payroll:incentives.form.coversEmployerUnemployment")}
                  checked={state.coversEmployerUnemployment}
                  onChange={(event) => patch({ coversEmployerUnemployment: event.currentTarget.checked })}
                />
                <Checkbox
                  label={t("payroll:incentives.form.coversEmployeeSgk")}
                  checked={state.coversEmployeeSgk}
                  onChange={(event) => patch({ coversEmployeeSgk: event.currentTarget.checked })}
                />
                <Checkbox
                  label={t("payroll:incentives.form.coversEmployeeUnemployment")}
                  checked={state.coversEmployeeUnemployment}
                  onChange={(event) => patch({ coversEmployeeUnemployment: event.currentTarget.checked })}
                />
              </Group>
            </div>
            {state.coversEmployerSgk && (
              <RateField
                label={t("payroll:incentives.form.employerSgkRate")}
                description={t("payroll:incentives.form.employerSgkRateHint")}
                value={state.employerSgkRate}
                onChange={(employerSgkRate) => patch({ employerSgkRate })}
                catalog={catalog}
                catalogFailed={catalogQuery.isError}
              />
            )}
          </>
        )}

        {kind === "declarationOnly" && (
          <Text size="sm" c="dimmed">
            {t("payroll:incentives.form.declarationOnlyHint")}
          </Text>
        )}

        <NumberInput
          label={t("payroll:incentives.form.maxMonths")}
          description={t("payroll:incentives.form.maxMonthsHint")}
          value={state.maxMonths}
          onChange={(maxMonths) => patch({ maxMonths })}
          allowDecimal={false}
          min={1}
          max={120}
        />

        <Group grow align="flex-start">
          <TextInput
            type="date"
            label={t("payroll:incentives.form.validFrom")}
            description={t("payroll:incentives.form.validFromHint")}
            value={state.validFrom}
            onChange={(event) => patch({ validFrom: event.currentTarget.value })}
          />
          <TextInput type="date" label={t("payroll:incentives.form.validTo")} value={state.validTo} onChange={(event) => patch({ validTo: event.currentTarget.value })} />
        </Group>

        <Text size="xs" c="dimmed">
          {t("payroll:incentives.form.draftHint")}
        </Text>

        {dropsApproval && (
          <Alert color="orange" data-testid="incentive-approval-drop-warning">
            {t("payroll:incentives.form.approvalWillDrop")}
          </Alert>
        )}
        {error && <Alert color="red">{error}</Alert>}

        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={!canSubmit}>
            {t("payroll:incentives.form.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

interface RateFieldProps {
  label: string;
  description?: string;
  value: RateState;
  onChange: (rate: RateState) => void;
  catalog: LegislationKeyValue[];
  catalogFailed: boolean;
}

/** Literal percent (%) entry OR a legislation parameter key from the catalog (its value in force today is shown). */
function RateField({ label, description, value, onChange, catalog, catalogFailed }: RateFieldProps) {
  const { t, i18n } = useTranslation(["payroll"]);
  const known = catalog.find((entry) => entry.key === value.key);
  const options = catalog.map((entry) => ({ value: entry.key, label: entry.key }));
  if (value.key && !known) options.push({ value: value.key, label: value.key });

  const formatValue = (amount: number) =>
    amount <= 1 ? `%${Number((amount * 100).toFixed(4)).toLocaleString(i18n.language)}` : amount.toLocaleString(i18n.language);

  return (
    <Stack gap={4}>
      <SegmentedControl
        size="xs"
        aria-label={t("payroll:incentives.form.rateMode", { label })}
        value={value.mode}
        onChange={(mode) => onChange({ ...value, mode: mode as RateState["mode"] })}
        data={[
          { value: "percent", label: t("payroll:incentives.form.modePercent") },
          { value: "key", label: t("payroll:incentives.form.modeKey") },
        ]}
      />
      {value.mode === "percent" ? (
        <NumberInput
          label={label}
          description={description}
          value={value.percent}
          onChange={(percent) => onChange({ ...value, percent })}
          decimalScale={4}
          min={0}
          max={100}
          rightSection={<Text size="sm">%</Text>}
        />
      ) : (
        <Select
          label={label}
          description={description}
          placeholder={t("payroll:incentives.form.keyPlaceholder")}
          data={options}
          value={value.key || null}
          onChange={(key) => onChange({ ...value, key: key ?? "" })}
          searchable
        />
      )}
      {value.mode === "key" && catalogFailed && (
        <Text size="xs" c="red">
          {t("payroll:incentives.form.catalogError")}
        </Text>
      )}
      {value.mode === "key" && value.key && (
        <Text size="xs" c="dimmed" data-testid="incentive-key-value">
          {known && known.value !== null && known.value !== undefined
            ? t("payroll:incentives.form.keyCurrentValue", { value: formatValue(known.value) })
            : t("payroll:incentives.form.keyNotInForce")}
        </Text>
      )}
    </Stack>
  );
}
