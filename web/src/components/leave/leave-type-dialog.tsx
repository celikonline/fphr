import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Button,
  Checkbox,
  Divider,
  Fieldset,
  Group,
  NumberInput,
  Select,
  Stack,
  Text,
  Textarea,
  TextInput,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { Plus, Trash2 } from "lucide-react";
import { useLeaveName } from "@/components/leave/leave-shared";
import { FormModal } from "@/components/ui/form-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  addLeaveType,
  updateLeaveType,
  type AccrualBasis,
  type AccrualFrequency,
  type AccrualTiming,
  type CarryOverMode,
  type LeaveEligibleGender,
  type LeaveLimitType,
  type LeaveRuleSet,
  type LeaveSeniorityTier,
  type LeaveType,
  type LeaveTypeCode,
  type LeaveTypeSettings,
  type LeaveUnit,
  type NegativeBalanceMode,
} from "@/services/leave.service";

const NAME_MAX_LENGTH = 200;
const MAX_DAYS = 365;
const MAX_WAITING_PERIOD_MONTHS = 24;

const CODES: LeaveTypeCode[] = [
  "custom",
  "annual",
  "excuse",
  "sick",
  "jobSearch",
  "marriage",
  "bereavement",
  "paternity",
  "maternity",
  "postnatalUnpaid",
  "paidMilitaryService",
  "unpaid",
  "fosterCare",
  "breastfeeding",
  "disabledChild",
  "remoteWork",
  "birthday",
];
const LIMIT_TYPES: LeaveLimitType[] = ["unlimited", "perRequest", "perYear"];
const NEGATIVE_BALANCE_MODES: NegativeBalanceMode[] = ["none", "unlimited", "limited"];
const ACCRUAL_FREQUENCIES: AccrualFrequency[] = ["yearly", "monthly", "daily"];
const ACCRUAL_TIMINGS: AccrualTiming[] = ["endOfPeriod", "startOfPeriod"];
const ACCRUAL_BASES: AccrualBasis[] = ["hireDate", "calendarYear"];
const CARRY_OVER_MODES: CarryOverMode[] = ["reset", "unlimited", "limited"];
const UNITS: LeaveUnit[] = ["fullDay", "halfDay", "hourly"];
const ELIGIBLE_GENDERS: LeaveEligibleGender[] = ["any", "female", "male"];

function defaultSettings(): LeaveTypeSettings {
  return {
    code: "custom",
    name: "",
    description: null,
    isPaid: true,
    limitType: "perYear",
    perRequestMaxDays: null,
    annualDays: 14,
    ageExceptionEnabled: false,
    ageExceptionMinDays: null,
    negativeBalance: "none",
    negativeBalanceLimitDays: null,
    accrualFrequency: "yearly",
    accrualTiming: "endOfPeriod",
    accrualBasis: "hireDate",
    carryOverMode: "reset",
    carryOverMaxDays: null,
    seniorityTiers: [],
    minConsecutiveDays: null,
    maxConsecutiveDays: null,
    unit: "fullDay",
    requiresDescription: false,
    requiresSubstitute: false,
    requiresDocument: false,
    waitingPeriodMonths: 0,
    countsNonWorkingDays: false,
    countsTowardAccrualService: true,
    showTypeNameOnCalendar: true,
    isSensitive: false,
    allowSelfServiceRequest: true,
    isDisabled: false,
    isStatutory: false,
    eligibleGender: "any",
  };
}

/**
 * "Yeni Tür ve Kural Ekle" / edit (11.3.5 #14): the leave type wizard's field set. New types are never statutory
 * (the API forces `isStatutory: false` regardless of what is sent), so that switch is not exposed here.
 */
export function LeaveTypeDialog({ ruleSet, editing, onClose }: { ruleSet: LeaveRuleSet; editing: LeaveType | null; onClose: () => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const isMobile = useMediaQuery("(max-width: 390px)");
  const [settings, setSettings] = useState<LeaveTypeSettings>(editing?.settings ?? defaultSettings());
  const [error, setError] = useState<string | null>(null);

  const set = <K extends keyof LeaveTypeSettings>(key: K, value: LeaveTypeSettings[K]) =>
    setSettings((current) => ({ ...current, [key]: value }));

  // `settings.name` may still be a seed resource key (e.g. "leave.type.annual") until the user edits the field;
  // the input always shows the resolved display value, and saving sends that resolved value (never the raw key) —
  // `leaveName` is a no-op once the user has typed a free-form name, so this is safe on every submit.
  const displayName = leaveName(settings.name);

  const mutation = useMutation({
    mutationFn: async () => {
      const payload = { ...settings, name: displayName };
      if (editing) await updateLeaveType(ruleSet.id, editing.id, payload);
      else await addLeaveType(ruleSet.id, payload);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["leave", "rule-sets"] });
      toast({ variant: "success", title: t("common:success"), description: t(editing ? "leave:types.saved" : "leave:types.added") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!displayName.trim()) return setError(t("leave:ruleSets.nameRequired"));
    setError(null);
    mutation.mutate();
  }

  const isPerRequest = settings.limitType === "perRequest";
  const isPerYear = settings.limitType === "perYear";
  const isAnnual = settings.code === "annual";
  const busy = mutation.isPending;

  return (
    <FormModal opened onClose={onClose} size="48rem" title={t(editing ? "leave:types.editTitle" : "leave:types.addTitle")} fullScreen={isMobile}>
      <form onSubmit={submit}>
        <Stack gap="lg">
          <Fieldset legend={t("leave:types.section.general")}>
            {/* senseik-038: fieldset varsayılan olarak min-content genişlikte boyutlanır; bu da doğrudan
                altındaki CSS Grid'in minmax(0,1fr) kolonlarıyla çakışıp grid'i 0px genişliğe çökertiyordu.
                w-full (width:100%) grid'i fieldset'in tam genişliğine zorlayarak çakışmayı çözer. */}
            <div className="grid w-full gap-4 sm:grid-cols-2">
              <Select
                id="leave-type-code"
                label={t("leave:ruleSets.col.code")}
                description={t("leave:types.codeHint")}
                data={CODES.map((code) => ({ value: code, label: t(`leave:code.${code}`) }))}
                value={settings.code}
                onChange={(value) => value && set("code", value as LeaveTypeCode)}
                allowDeselect={false}
                disabled={busy}
                inputWrapperOrder={["label", "input", "description"]}
              />
              <TextInput
                id="leave-type-name"
                label={t("leave:types.name")}
                withAsterisk
                value={displayName}
                maxLength={NAME_MAX_LENGTH}
                disabled={busy}
                onChange={(event) => set("name", event.currentTarget.value)}
              />
            </div>
            <Textarea
              id="leave-type-description"
              label={t("leave:types.description")}
              mt="md"
              autosize
              minRows={2}
              value={settings.description ?? ""}
              disabled={busy}
              onChange={(event) => set("description", event.currentTarget.value || null)}
            />
            <Checkbox
              mt="md"
              label={t("leave:types.isPaid")}
              checked={settings.isPaid}
              disabled={busy}
              onChange={(event) => set("isPaid", event.currentTarget.checked)}
            />
          </Fieldset>

          <Fieldset legend={t("leave:types.section.limit")}>
            <Select
              id="leave-type-limitType"
              label={t("leave:ruleSets.col.limit")}
              data={LIMIT_TYPES.map((option) => ({ value: option, label: t(`leave:limitType.${option}`) }))}
              value={settings.limitType}
              onChange={(value) => value && set("limitType", value as LeaveLimitType)}
              allowDeselect={false}
              disabled={busy}
            />

            {isPerRequest && (
              <NumberInput
                id="leave-type-perRequestMaxDays"
                mt="md"
                label={t("leave:types.perRequestMaxDays")}
                min={0.5}
                max={MAX_DAYS}
                step={0.5}
                value={settings.perRequestMaxDays ?? undefined}
                disabled={busy}
                onChange={(value) => set("perRequestMaxDays", typeof value === "number" ? value : null)}
              />
            )}

            {isPerYear && (
              <Stack gap="md" mt="md">
                <NumberInput
                  id="leave-type-annualDays"
                  label={t("leave:types.annualDays")}
                  min={0.5}
                  max={MAX_DAYS}
                  step={0.5}
                  withAsterisk
                  value={settings.annualDays ?? undefined}
                  disabled={busy}
                  onChange={(value) => set("annualDays", typeof value === "number" ? value : null)}
                />

                <Group align="flex-end" gap="md">
                  <Checkbox
                    label={t("leave:types.ageExceptionEnabled")}
                    checked={settings.ageExceptionEnabled}
                    disabled={busy}
                    onChange={(event) => set("ageExceptionEnabled", event.currentTarget.checked)}
                  />
                  {settings.ageExceptionEnabled && (
                    <NumberInput
                      id="leave-type-ageExceptionMinDays"
                      label={t("leave:types.ageExceptionMinDays")}
                      min={0.5}
                      max={MAX_DAYS}
                      step={0.5}
                      value={settings.ageExceptionMinDays ?? undefined}
                      disabled={busy}
                      onChange={(value) => set("ageExceptionMinDays", typeof value === "number" ? value : null)}
                    />
                  )}
                </Group>

                <div className="grid w-full gap-4 sm:grid-cols-2">
                  <Select
                    id="leave-type-negativeBalance"
                    label={t("leave:types.negativeBalance")}
                    data={NEGATIVE_BALANCE_MODES.map((option) => ({ value: option, label: t(`leave:negativeBalance.${option}`) }))}
                    value={settings.negativeBalance}
                    onChange={(value) => value && set("negativeBalance", value as NegativeBalanceMode)}
                    allowDeselect={false}
                    disabled={busy}
                  />
                  {settings.negativeBalance === "limited" && (
                    <NumberInput
                      id="leave-type-negativeBalanceLimitDays"
                      label={t("leave:types.negativeBalanceLimitDays")}
                      min={0.5}
                      max={settings.annualDays ?? MAX_DAYS}
                      step={0.5}
                      value={settings.negativeBalanceLimitDays ?? undefined}
                      disabled={busy}
                      onChange={(value) => set("negativeBalanceLimitDays", typeof value === "number" ? value : null)}
                    />
                  )}
                </div>

                <div className="grid w-full gap-4 sm:grid-cols-3">
                  <Select
                    id="leave-type-accrualFrequency"
                    label={t("leave:ruleSets.col.accrual")}
                    data={ACCRUAL_FREQUENCIES.map((option) => ({ value: option, label: t(`leave:accrualFrequency.${option}`) }))}
                    value={settings.accrualFrequency}
                    onChange={(value) => value && set("accrualFrequency", value as AccrualFrequency)}
                    allowDeselect={false}
                    disabled={busy}
                  />
                  <Select
                    id="leave-type-accrualTiming"
                    label={t("leave:types.accrualTiming")}
                    data={ACCRUAL_TIMINGS.map((option) => ({ value: option, label: t(`leave:accrualTiming.${option}`) }))}
                    value={settings.accrualTiming}
                    onChange={(value) => value && set("accrualTiming", value as AccrualTiming)}
                    allowDeselect={false}
                    disabled={busy}
                  />
                  <Select
                    id="leave-type-accrualBasis"
                    label={t("leave:types.accrualBasis")}
                    data={ACCRUAL_BASES.map((option) => ({ value: option, label: t(`leave:accrualBasis.${option}`) }))}
                    value={settings.accrualBasis}
                    onChange={(value) => value && set("accrualBasis", value as AccrualBasis)}
                    allowDeselect={false}
                    disabled={busy}
                  />
                </div>

                <div className="grid w-full gap-4 sm:grid-cols-2">
                  <Select
                    id="leave-type-carryOverMode"
                    label={t("leave:ruleSets.col.carryOver")}
                    data={CARRY_OVER_MODES.map((option) => ({ value: option, label: t(`leave:carryOverMode.${option}`) }))}
                    value={settings.carryOverMode}
                    onChange={(value) => value && set("carryOverMode", value as CarryOverMode)}
                    allowDeselect={false}
                    disabled={busy}
                  />
                  {settings.carryOverMode === "limited" && (
                    <NumberInput
                      id="leave-type-carryOverMaxDays"
                      label={t("leave:types.carryOverMaxDays")}
                      min={0.5}
                      max={MAX_DAYS}
                      step={0.5}
                      value={settings.carryOverMaxDays ?? undefined}
                      disabled={busy}
                      onChange={(value) => set("carryOverMaxDays", typeof value === "number" ? value : null)}
                    />
                  )}
                </div>

                {isAnnual && (
                  <SeniorityTiersEditor
                    tiers={settings.seniorityTiers ?? []}
                    onChange={(tiers) => set("seniorityTiers", tiers)}
                    disabled={busy}
                  />
                )}
              </Stack>
            )}
          </Fieldset>

          <Fieldset legend={t("leave:types.section.rules")}>
            <div className="grid w-full gap-4 sm:grid-cols-3">
              <NumberInput
                id="leave-type-minConsecutiveDays"
                label={t("leave:types.minConsecutiveDays")}
                min={0}
                max={MAX_DAYS}
                step={0.5}
                value={settings.minConsecutiveDays ?? undefined}
                disabled={busy}
                onChange={(value) => set("minConsecutiveDays", typeof value === "number" ? value : null)}
              />
              <NumberInput
                id="leave-type-maxConsecutiveDays"
                label={t("leave:types.maxConsecutiveDays")}
                min={0}
                max={MAX_DAYS}
                step={0.5}
                value={settings.maxConsecutiveDays ?? undefined}
                disabled={busy}
                onChange={(value) => set("maxConsecutiveDays", typeof value === "number" ? value : null)}
              />
              <Select
                id="leave-type-unit"
                label={t("leave:ruleSets.col.unit")}
                data={UNITS.map((option) => ({ value: option, label: t(`leave:unit.${option}`) }))}
                value={settings.unit}
                onChange={(value) => value && set("unit", value as LeaveUnit)}
                allowDeselect={false}
                disabled={busy}
              />
            </div>
            <Select
              id="leave-type-eligibleGender"
              mt="md"
              label={t("leave:types.eligibleGender")}
              description={t("leave:types.eligibleGenderHint")}
              data={ELIGIBLE_GENDERS.map((option) => ({ value: option, label: t(`leave:eligibleGender.${option}`) }))}
              value={settings.eligibleGender ?? "any"}
              onChange={(value) => value && set("eligibleGender", value as LeaveEligibleGender)}
              allowDeselect={false}
              disabled={busy}
              inputWrapperOrder={["label", "input", "description"]}
            />
            <NumberInput
              id="leave-type-waitingPeriodMonths"
              mt="md"
              label={t("leave:types.waitingPeriodMonths")}
              description={t("leave:types.waitingPeriodMonthsHint")}
              min={0}
              max={MAX_WAITING_PERIOD_MONTHS}
              value={settings.waitingPeriodMonths}
              disabled={busy}
              onChange={(value) => set("waitingPeriodMonths", typeof value === "number" ? value : 0)}
              inputWrapperOrder={["label", "input", "description"]}
            />
            <Divider my="md" />
            <div className="grid w-full gap-2 sm:grid-cols-2">
              <Checkbox
                label={t("leave:types.requiresDescription")}
                checked={settings.requiresDescription}
                disabled={busy}
                onChange={(event) => set("requiresDescription", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.requiresSubstitute")}
                checked={settings.requiresSubstitute}
                disabled={busy}
                onChange={(event) => set("requiresSubstitute", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.requiresDocument")}
                checked={settings.requiresDocument}
                disabled={busy}
                onChange={(event) => set("requiresDocument", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.countsNonWorkingDays")}
                checked={settings.countsNonWorkingDays}
                disabled={busy}
                onChange={(event) => set("countsNonWorkingDays", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.countsTowardAccrualService")}
                checked={settings.countsTowardAccrualService}
                disabled={busy}
                onChange={(event) => set("countsTowardAccrualService", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.showTypeNameOnCalendar")}
                checked={settings.showTypeNameOnCalendar}
                disabled={busy}
                onChange={(event) => set("showTypeNameOnCalendar", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.isSensitive")}
                checked={settings.isSensitive}
                disabled={busy}
                onChange={(event) => set("isSensitive", event.currentTarget.checked)}
              />
              <Checkbox
                label={t("leave:types.allowSelfServiceRequest")}
                checked={settings.allowSelfServiceRequest}
                disabled={busy}
                onChange={(event) => set("allowSelfServiceRequest", event.currentTarget.checked)}
              />
            </div>
          </Fieldset>

          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}

          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}

/** "Kıdem kademesi" rows (Annual type only, PerYear limit): completed years vs extra days, cumulative. */
function SeniorityTiersEditor({
  tiers,
  onChange,
  disabled,
}: {
  tiers: LeaveSeniorityTier[];
  onChange: (tiers: LeaveSeniorityTier[]) => void;
  disabled: boolean;
}) {
  const { t } = useTranslation(["leave", "common"]);

  const update = (index: number, patch: Partial<LeaveSeniorityTier>) =>
    onChange(tiers.map((tier, i) => (i === index ? { ...tier, ...patch } : tier)));

  return (
    <div>
      <Group justify="space-between" mb="xs">
        <Text size="sm" fw={500}>
          {t("leave:types.seniorityTiers")}
        </Text>
        <Button
          type="button"
          size="xs"
          variant="subtle"
          leftSection={<Plus size={14} />}
          disabled={disabled}
          onClick={() => onChange([...tiers, { afterYears: 1, comparison: "greaterOrEqual", extraDays: 1 }])}
        >
          {t("leave:types.addTier")}
        </Button>
      </Group>
      {tiers.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:types.noTiers")}
        </Text>
      ) : (
        <Stack gap="xs">
          {tiers.map((tier, index) => (
            <Group key={index} align="flex-end" gap="sm" wrap="wrap">
              <NumberInput
                label={t("leave:types.tierAfterYears")}
                min={0}
                max={60}
                value={tier.afterYears}
                disabled={disabled}
                onChange={(value) => update(index, { afterYears: typeof value === "number" ? value : 0 })}
                className="w-full sm:w-32"
              />
              <Select
                label={t("leave:types.tierComparison")}
                data={[
                  { value: "greaterOrEqual", label: t("leave:types.tierGreaterOrEqual") },
                  { value: "greaterThan", label: t("leave:types.tierGreaterThan") },
                ]}
                value={tier.comparison}
                onChange={(value) => value && update(index, { comparison: value as LeaveSeniorityTier["comparison"] })}
                allowDeselect={false}
                disabled={disabled}
                className="w-full sm:w-48"
              />
              <NumberInput
                label={t("leave:types.tierExtraDays")}
                min={0}
                max={MAX_DAYS}
                step={0.5}
                value={tier.extraDays}
                disabled={disabled}
                onChange={(value) => update(index, { extraDays: typeof value === "number" ? value : 0 })}
                className="w-full sm:w-32"
              />
              <ActionIcon
                type="button"
                variant="subtle"
                color="red"
                size="lg"
                className="ml-auto sm:ml-0"
                aria-label={t("common:delete")}
                disabled={disabled}
                onClick={() => onChange(tiers.filter((_, i) => i !== index))}
              >
                <Trash2 size={16} />
              </ActionIcon>
            </Group>
          ))}
        </Stack>
      )}
    </div>
  );
}
