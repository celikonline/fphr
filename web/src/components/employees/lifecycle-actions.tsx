import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Checkbox, Group, Input, Modal, Select, Stack, Text, TextInput } from "@mantine/core";
import { AlertTriangle } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { getEmployeeAssets } from "@/services/assets.service";
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

/** "İşten ayrılış sihirbazı": reason pre-fills the type and SGK code, and the exit settlement flags close the period. */
function TerminateDialog({ employee, onClose, onDone }: StepProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const [terminationDate, setTerminationDate] = useState(todayDateOnly());
  const [reasonId, setReasonId] = useState<string | null>(null);
  const [type, setType] = useState<TerminationType | null>(null);
  const [sgkExitCode, setSgkExitCode] = useState("");
  const [severancePaid, setSeverancePaid] = useState(false);
  const [leaveSettledAtExit, setLeaveSettledAtExit] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data: reasons = [] } = useQuery({ queryKey: ["termination-reasons"], queryFn: () => getTerminationReasons() });
  const { data: collective } = useQuery({
    queryKey: ["collective-dismissal", terminationDate],
    queryFn: () => getCollectiveDismissalStatus(terminationDate),
    enabled: Boolean(terminationDate),
  });

  // Offboarding <-> zimmet (roadmap): advisory only - shown so HR sees it up front, but never blocks the wizard.
  const canViewAssets = usePermission("assets.assets.view");
  const { data: assets = [] } = useQuery({
    queryKey: ["assets", "employee-assets", employee.id],
    queryFn: () => getEmployeeAssets(employee.id),
    enabled: canViewAssets,
  });
  const openAssetCount = assets.filter((asset) => asset.status === "assigned").length;

  /** Picking a reason pre-fills the type and SGK code; both stay editable afterwards. */
  function chooseReason(value: string | null) {
    setReasonId(value);
    const picked = reasons.find((candidate) => candidate.id === value);
    if (picked) {
      setType(picked.defaultTerminationType);
      setSgkExitCode(picked.defaultSgkExitCode ?? "");
    }
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

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!terminationDate) return setError(t("employees:lifecycle.terminate.dateRequired"));
    if (!reasonId) return setError(t("employees:lifecycle.terminate.reasonRequired"));
    if (employee.hireDate && terminationDate < employee.hireDate)
      return setError(t("employees:lifecycle.terminate.dateBeforeHire", { date: formatDateOnly(employee.hireDate, i18n.language) }));

    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("employees:lifecycle.terminate.title")} size="42rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:lifecycle.terminate.description")}
          </Text>
          {collective?.showWarning && collective.warningMessage && (
            <Alert color="orange" icon={<AlertTriangle size={16} />} title={t("employees:lifecycle.terminate.collectiveTitle")}>
              {collective.warningMessage}
            </Alert>
          )}
          {openAssetCount > 0 && (
            <Alert color="orange" icon={<AlertTriangle size={16} />} title={t("employees:lifecycle.terminate.openAssetsTitle")}>
              {t("employees:lifecycle.terminate.openAssetsWarning", { count: openAssetCount })}
            </Alert>
          )}
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
            <TextInput
              id="terminate-sgk"
              label={t("employees:lifecycle.terminate.sgkExitCode")}
              value={sgkExitCode}
              maxLength={20}
              onChange={(event) => setSgkExitCode(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
          </div>
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
          <Actions onClose={onClose} pending={mutation.isPending} submitLabel={t("employees:lifecycle.terminate.submit")} danger />
        </Stack>
      </form>
    </Modal>
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
