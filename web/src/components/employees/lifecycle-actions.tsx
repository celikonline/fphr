import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Checkbox, Group, Input, Modal, Select, Stack, Text } from "@mantine/core";
import { AlertTriangle } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { OpenAssetsWarning } from "@/components/assets/open-assets-warning";
import { toast } from "@/hooks/use-toast";
import { useModuleEnabled } from "@/hooks/use-module-enabled";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import {
  getCollectiveDismissalStatus,
  getEmploymentPeriods,
  getTerminationReasons,
  reactivateEmployee,
  rehireEmployee,
  suspendEmployee,
  terminateEmployee,
  SUSPENSION_REASONS,
  TERMINATION_TYPES,
  type EmployeeDetail,
  type EmployeeStatus,
  type SuspensionReason,
  type TerminationType,
} from "@/services/employees.service";
import { getSeveranceEstimate, type SeveranceCauseCategory } from "@/services/payroll-severance.service";

/** Lifecycle transitions offered on the employee card ("İşten ayrılış sihirbazı" and friends, E5a). */
export type LifecycleAction = "suspend" | "reactivate" | "terminate" | "rehire";

/** Which transitions the current status allows; Draft employees have none (they are not hired yet). */
export function lifecycleActionsFor(status: EmployeeStatus): LifecycleAction[] {
  switch (status) {
    case "Active":
      return ["suspend", "terminate"];
    case "Suspended":
      return ["reactivate", "terminate"];
    case "Terminated":
      return ["rehire"];
    default:
      return [];
  }
}

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

interface DialogProps {
  employee: EmployeeDetail;
  action: LifecycleAction | null;
  onClose: () => void;
}

/**
 * The dialogs behind {@link lifecycleActionsFor}. Rendered by the card header outside its actions menu, because a
 * modal mounted inside a Menu.Dropdown is unmounted the moment the menu closes.
 */
export function EmployeeLifecycleDialogs({ employee, action, onClose }: DialogProps) {
  const queryClient = useQueryClient();
  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ["employees"] });
    queryClient.invalidateQueries({ queryKey: ["employment-periods", employee.id] });
  };

  if (action === "suspend") return <SuspendDialog employee={employee} onClose={onClose} onDone={refresh} />;
  if (action === "reactivate") return <ReactivateDialog employee={employee} onClose={onClose} onDone={refresh} />;
  if (action === "terminate") return <TerminateDialog employee={employee} onClose={onClose} onDone={refresh} />;
  if (action === "rehire") return <RehireDialog employee={employee} onClose={onClose} onDone={refresh} />;
  return null;
}

interface StepProps {
  employee: EmployeeDetail;
  onClose: () => void;
  onDone: () => void;
}

function SuspendDialog({ employee, onClose, onDone }: StepProps) {
  const { t } = useTranslation(["employees", "common"]);
  const [reason, setReason] = useState<SuspensionReason>("MilitaryService");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => suspendEmployee(employee.id, reason),
    onSuccess: () => {
      onDone();
      toast({ variant: "success", title: t("common:success"), description: t("employees:lifecycle.suspend.success") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  return (
    <Modal opened onClose={onClose} title={t("employees:lifecycle.suspend.title")} size="30rem">
      <form
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          setError(null);
          mutation.mutate();
        }}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:lifecycle.suspend.description")}
          </Text>
          <Select
            id="suspend-reason"
            label={t("employees:lifecycle.suspend.reason")}
            data={SUSPENSION_REASONS.map((option) => ({ value: option, label: t(`employees:enum.suspensionReason.${option}`) }))}
            value={reason}
            onChange={(value) => value && setReason(value as SuspensionReason)}
            allowDeselect={false}
            disabled={mutation.isPending}
          />
          {error && <ErrorAlert message={error} />}
          <Actions onClose={onClose} pending={mutation.isPending} submitLabel={t("employees:lifecycle.suspend.submit")} />
        </Stack>
      </form>
    </Modal>
  );
}

function ReactivateDialog({ employee, onClose, onDone }: StepProps) {
  const { t } = useTranslation(["employees", "common"]);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => reactivateEmployee(employee.id),
    onSuccess: () => {
      onDone();
      toast({ variant: "success", title: t("common:success"), description: t("employees:lifecycle.reactivate.success") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  return (
    <Modal opened onClose={onClose} title={t("employees:lifecycle.reactivate.title")} size="30rem">
      <Stack gap="md">
        <Text size="sm">{t("employees:lifecycle.reactivate.description", { name: employee.displayName })}</Text>
        {error && <ErrorAlert message={error} />}
        <Actions
          onClose={onClose}
          pending={mutation.isPending}
          submitLabel={t("employees:lifecycle.reactivate.submit")}
          onSubmit={() => {
            setError(null);
            mutation.mutate();
          }}
        />
      </Stack>
    </Modal>
  );
}

/**
 * "İşten ayrılış sihirbazı": reason pre-fills the type and SGK code. The code is shown read-only (senseik-006/F-01 -
 * it must come from the verified reason catalog, not free text); a reason whose type covers more than one SGK code
 * (md.24/md.25 families) requires an explicit second pick instead of a silently-wrong default. Step 2 makes the
 * resolved code part of an explicit confirmation before the exit is submitted.
 */
function TerminateDialog({ employee, onClose, onDone }: StepProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const [step, setStep] = useState<1 | 2>(1);
  const [terminationDate, setTerminationDate] = useState(todayDateOnly());
  const [reasonId, setReasonId] = useState<string | null>(null);
  const [type, setType] = useState<TerminationType | null>(null);
  const [sgkExitCode, setSgkExitCode] = useState("");
  const [severancePaid, setSeverancePaid] = useState(false);
  const [leaveSettledAtExit, setLeaveSettledAtExit] = useState(false);
  const [sgkExitCodeConfirmed, setSgkExitCodeConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data: reasons = [] } = useQuery({ queryKey: ["termination-reasons"], queryFn: () => getTerminationReasons() });
  const { data: collective } = useQuery({
    queryKey: ["collective-dismissal", terminationDate],
    queryFn: () => getCollectiveDismissalStatus(terminationDate),
    enabled: Boolean(terminationDate),
  });

  const selectedReason = reasons.find((candidate) => candidate.id === reasonId) ?? null;
  const subCodeOptions = selectedReason?.allowedSgkExitCodes ?? null;
  const requiresSubCodePick = Boolean(subCodeOptions && subCodeOptions.length > 1);

  /**
   * Picking a reason pre-fills the type; the SGK code follows the reason's family (backend-derived
   * allowedSgkExitCodes, never redefined here) - a single-code family fills it in automatically, an ambiguous one
   * (md.24/md.25) is left blank until the mandatory sub-code select below is used.
   */
  function chooseReason(value: string | null) {
    setReasonId(value);
    const picked = reasons.find((candidate) => candidate.id === value);
    if (!picked) return;
    setType(picked.defaultTerminationType);
    const allowed = picked.allowedSgkExitCodes;
    setSgkExitCode(allowed && allowed.length === 1 ? allowed[0] : (picked.defaultSgkExitCode ?? ""));
  }

  const mutation = useMutation({
    mutationFn: () =>
      terminateEmployee(employee.id, {
        terminationDate,
        terminationReasonId: reasonId as string,
        terminationType: type,
        sgkExitCode: sgkExitCode.trim() || null,
        severancePaid,
        leaveSettledAtExit,
      }),
    onSuccess: (result) => {
      onDone();
      toast({ variant: "success", title: t("common:success"), description: t("employees:lifecycle.terminate.success") });
      const warnings = [result.jobSecurityWarningMessage, result.collectiveDismissal?.warningMessage].filter(Boolean);
      for (const message of warnings) {
        toast({ variant: "default", title: t("employees:lifecycle.terminate.warningTitle"), description: message as string });
      }
      if (result.openAssetCount > 0) {
        toast({
          variant: "default",
          title: t("employees:lifecycle.terminate.openAssetsTitle"),
          description: t("employees:lifecycle.terminate.openAssetsWarning", { count: result.openAssetCount }),
        });
      }
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  function goToReview(event: FormEvent) {
    event.preventDefault();
    if (!terminationDate) return setError(t("employees:lifecycle.terminate.dateRequired"));
    if (!reasonId) return setError(t("employees:lifecycle.terminate.reasonRequired"));
    if (employee.hireDate && terminationDate < employee.hireDate)
      return setError(t("employees:lifecycle.terminate.dateBeforeHire", { date: formatDateOnly(employee.hireDate, i18n.language) }));
    if (requiresSubCodePick && !sgkExitCode) return setError(t("employees:lifecycle.terminate.sgkSubCodeRequired"));

    setError(null);
    setStep(2);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!sgkExitCodeConfirmed) return;
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("employees:lifecycle.terminate.title")} size="42rem">
      {step === 1 ? (
        <form onSubmit={goToReview}>
          <Stack gap="md">
            <Text size="sm" c="dimmed">
              {t("employees:lifecycle.terminate.description")}
            </Text>
            {collective?.showWarning && collective.warningMessage && (
              <Alert color="orange" icon={<AlertTriangle size={16} />} title={t("employees:lifecycle.terminate.collectiveTitle")}>
                {collective.warningMessage}
              </Alert>
            )}
            <OpenAssetsWarning employeeId={employee.id} />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input.Wrapper id="terminate-date" label={t("employees:lifecycle.terminate.date")} withAsterisk>
                <DatePicker id="terminate-date" value={terminationDate} onChange={setTerminationDate} disabled={mutation.isPending} />
              </Input.Wrapper>
              <Select
                id="terminate-reason"
                label={t("employees:lifecycle.terminate.reason")}
                withAsterisk
                searchable
                data={reasons.map((option) => ({ value: option.id, label: option.name }))}
                value={reasonId}
                onChange={chooseReason}
                disabled={mutation.isPending}
              />
              <Select
                id="terminate-type"
                label={t("employees:lifecycle.terminate.type")}
                description={t("employees:lifecycle.terminate.typeHint")}
                data={TERMINATION_TYPES.map((option) => ({ value: option, label: t(`employees:enum.terminationType.${option}`) }))}
                value={type}
                onChange={(value) => setType(value as TerminationType | null)}
                disabled={mutation.isPending}
                inputWrapperOrder={["label", "input", "description"]}
              />
              <Input.Wrapper
                id="terminate-sgk"
                label={t("employees:lifecycle.terminate.sgkExitCode")}
                description={t("employees:lifecycle.terminate.sgkExitCodeHint")}
                inputWrapperOrder={["label", "input", "description"]}
              >
                <Text size="sm" fw={500} py={6}>
                  {sgkExitCode || "—"}
                </Text>
              </Input.Wrapper>
              {requiresSubCodePick && (
                <Select
                  id="terminate-sgk-subcode"
                  label={t("employees:lifecycle.terminate.sgkSubCode")}
                  description={t("employees:lifecycle.terminate.sgkSubCodeHint")}
                  withAsterisk
                  data={(subCodeOptions ?? []).map((code) => ({ value: code, label: code }))}
                  value={sgkExitCode || null}
                  onChange={(value) => setSgkExitCode(value ?? "")}
                  allowDeselect={false}
                  disabled={mutation.isPending}
                  inputWrapperOrder={["label", "input", "description"]}
                />
              )}
            </div>
            <SeveranceEstimatePanel employeeId={employee.id} terminationDate={terminationDate} terminationType={type} />
            <Checkbox
              label={t("employees:lifecycle.terminate.severancePaid")}
              description={t("employees:lifecycle.terminate.severancePaidHint")}
              checked={severancePaid}
              onChange={(event) => setSeverancePaid(event.currentTarget.checked)}
              disabled={mutation.isPending}
            />
            <Checkbox
              label={t("employees:lifecycle.terminate.leaveSettled")}
              description={t("employees:lifecycle.terminate.leaveSettledHint")}
              checked={leaveSettledAtExit}
              onChange={(event) => setLeaveSettledAtExit(event.currentTarget.checked)}
              disabled={mutation.isPending}
            />
            {error && <ErrorAlert message={error} />}
            <Group justify="flex-end" gap="sm">
              <Button type="button" variant="default" onClick={onClose}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" color="red">
                {t("employees:lifecycle.terminate.next")}
              </Button>
            </Group>
          </Stack>
        </form>
      ) : (
        <form onSubmit={submit}>
          <Stack gap="md">
            <Text size="sm" c="dimmed">
              {t("employees:lifecycle.terminate.reviewDescription")}
            </Text>
            <Stack gap={4}>
              <SummaryRow label={t("employees:lifecycle.terminate.date")} value={formatDateOnly(terminationDate, i18n.language)} />
              <SummaryRow label={t("employees:lifecycle.terminate.reason")} value={selectedReason?.name ?? "—"} />
              <SummaryRow
                label={t("employees:lifecycle.terminate.type")}
                value={type ? t(`employees:enum.terminationType.${type}`) : "—"}
              />
              <SummaryRow label={t("employees:lifecycle.terminate.sgkExitCode")} value={sgkExitCode || "—"} />
            </Stack>
            <Checkbox
              id="terminate-confirm-sgk"
              label={t("employees:lifecycle.terminate.confirmSgkExitCode")}
              checked={sgkExitCodeConfirmed}
              onChange={(event) => setSgkExitCodeConfirmed(event.currentTarget.checked)}
              disabled={mutation.isPending}
            />
            {error && <ErrorAlert message={error} />}
            <Group justify="flex-end" gap="sm">
              <Button type="button" variant="default" onClick={() => setStep(1)} disabled={mutation.isPending}>
                {t("common:back")}
              </Button>
              <Button type="submit" loading={mutation.isPending} color="red" disabled={!sgkExitCodeConfirmed}>
                {t("employees:lifecycle.terminate.submit")}
              </Button>
            </Group>
          </Stack>
        </form>
      )}
    </Modal>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <Group justify="space-between" gap="sm" wrap="nowrap">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={500} ta="right">
        {value}
      </Text>
    </Group>
  );
}

/**
 * Coarser than {@link TerminationType}: the wizard has no field for İş K. m.25/II (ahlak ve iyiniyet kurallarına
 * aykırılık, the one cause that forfeits both kıdem and ihbar tazminatı), so the panel asks for it locally only
 * when the picked type is employerJustCause_Art25 - the answer feeds this read-only estimate only, never the
 * termination command itself.
 */
function mapCauseCategory(type: TerminationType | null, art25IsImmoral: boolean): SeveranceCauseCategory {
  switch (type) {
    case "validCause_Art18":
      return "employerNoticeBased";
    case "employeeJustCause_Art24":
      return "employeeJustCause";
    case "employerJustCause_Art25":
      return art25IsImmoral ? "immoralOrBadFaith" : "employerJustCauseOther";
    case "mutualRescission":
    case "resignation":
    case "fixedTermExpiry":
      return "employeeResignation";
    case "other":
    default:
      return "employerNoticeBased";
  }
}

/**
 * "İşten ayrılış sihirbazı" salt-okunur özet blok (senseik-011, F-06): kıdem/ihbar/kullanılmamış izin ücreti
 * tahmini. Nothing here is submitted with the termination - a preview only, recomputed as the date/type change.
 */
function SeveranceEstimatePanel({
  employeeId,
  terminationDate,
  terminationType,
}: {
  employeeId: string;
  terminationDate: string;
  terminationType: TerminationType | null;
}) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const [art25IsImmoral, setArt25IsImmoral] = useState(false);
  const causeCategory = mapCauseCategory(terminationType, art25IsImmoral);
  // senseik-057: the estimate comes from Payroll; skip it when the tenant turned that module off.
  const payrollEnabled = useModuleEnabled("payroll");

  const { data, isLoading, isError } = useQuery({
    queryKey: ["severance-estimate", employeeId, terminationDate, causeCategory],
    queryFn: () => getSeveranceEstimate(employeeId, terminationDate, causeCategory),
    enabled: payrollEnabled && Boolean(employeeId && terminationDate),
    retry: false,
  });

  if (!terminationDate || !payrollEnabled) {
    return null;
  }

  const money = (amount: number) => (data ? formatMoney(amount, data.currency, i18n.language) : "—");

  return (
    <Stack gap="xs" p="sm" style={{ border: "1px solid var(--mantine-color-gray-3)", borderRadius: "var(--mantine-radius-sm)" }}>
      <Text size="sm" fw={600}>
        {t("employees:lifecycle.terminate.severanceEstimate.title")}
      </Text>
      {terminationType === "employerJustCause_Art25" && (
        <Checkbox
          size="sm"
          label={t("employees:lifecycle.terminate.severanceEstimate.immoralOrBadFaith")}
          checked={art25IsImmoral}
          onChange={(event) => setArt25IsImmoral(event.currentTarget.checked)}
        />
      )}
      {isLoading && (
        <Text size="xs" c="dimmed">
          {t("common:loading")}
        </Text>
      )}
      {isError && (
        <Text size="xs" c="dimmed">
          {t("employees:lifecycle.terminate.severanceEstimate.unavailable")}
        </Text>
      )}
      {data && (
        <Stack gap={4}>
          <SummaryRow label={t("employees:lifecycle.terminate.severanceEstimate.severance")} value={money(data.severanceAmount)} />
          <SummaryRow label={t("employees:lifecycle.terminate.severanceEstimate.notice")} value={money(data.noticeAmount)} />
          <SummaryRow label={t("employees:lifecycle.terminate.severanceEstimate.unusedLeave")} value={money(data.unusedLeaveAmount)} />
          <SummaryRow label={t("employees:lifecycle.terminate.severanceEstimate.total")} value={money(data.totalAmount)} />
        </Stack>
      )}
      <Text size="xs" c="dimmed">
        {t("employees:lifecycle.terminate.severanceEstimate.disclaimer")}
      </Text>
    </Stack>
  );
}

/** "Yeniden işe al": the merge checkbox decides whether the counted previous periods move the leave accrual start back. */
function RehireDialog({ employee, onClose, onDone }: StepProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const [hireDate, setHireDate] = useState(todayDateOnly());
  const [merge, setMerge] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const { data: history } = useQuery({
    queryKey: ["employment-periods", employee.id],
    queryFn: () => getEmploymentPeriods(employee.id),
  });

  const previousDays = history?.seniority.leaveSeniorityDays ?? 0;

  const mutation = useMutation({
    mutationFn: () => rehireEmployee(employee.id, { hireDate, mergePreviousPeriods: merge, leaveAccrualStartDate: null }),
    onSuccess: (result) => {
      onDone();
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:lifecycle.rehire.success", {
          date: formatDateOnly(result.employee.leaveAccrualStartDate ?? hireDate, i18n.language),
        }),
      });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!hireDate) return setError(t("employees:lifecycle.rehire.dateRequired"));
    if (employee.terminationDate && hireDate <= employee.terminationDate)
      return setError(t("employees:lifecycle.rehire.dateBeforeExit", { date: formatDateOnly(employee.terminationDate, i18n.language) }));

    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("employees:lifecycle.rehire.title")} size="36rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:lifecycle.rehire.description")}
          </Text>
          <Input.Wrapper id="rehire-date" label={t("employees:lifecycle.rehire.hireDate")} withAsterisk>
            <DatePicker id="rehire-date" value={hireDate} onChange={setHireDate} disabled={mutation.isPending} />
          </Input.Wrapper>
          <Checkbox
            label={t("employees:lifecycle.rehire.merge")}
            description={t("employees:lifecycle.rehire.mergeHint", { days: previousDays })}
            checked={merge}
            onChange={(event) => setMerge(event.currentTarget.checked)}
            disabled={mutation.isPending}
          />
          {error && <ErrorAlert message={error} />}
          <Actions onClose={onClose} pending={mutation.isPending} submitLabel={t("employees:lifecycle.rehire.submit")} />
        </Stack>
      </form>
    </Modal>
  );
}

function ErrorAlert({ message }: { message: string }) {
  return (
    <Alert color="red" icon={<AlertTriangle size={16} />}>
      {message}
    </Alert>
  );
}

function Actions({
  onClose,
  pending,
  submitLabel,
  onSubmit,
  danger,
}: {
  onClose: () => void;
  pending: boolean;
  submitLabel: string;
  onSubmit?: () => void;
  danger?: boolean;
}) {
  const { t } = useTranslation(["common"]);
  return (
    <Group justify="flex-end" gap="sm">
      <Button type="button" variant="default" onClick={onClose} disabled={pending}>
        {t("common:cancel")}
      </Button>
      <Button type={onSubmit ? "button" : "submit"} onClick={onSubmit} loading={pending} color={danger ? "red" : undefined}>
        {submitLabel}
      </Button>
    </Group>
  );
}
