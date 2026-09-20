import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Plus, Trash2, Workflow as WorkflowIcon } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  NavLink,
  Paper,
  Select,
  Skeleton,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { getEmployee } from "@/services/employees.service";
import {
  createPolicy,
  deactivatePolicy,
  listPolicies,
  updatePolicy,
  type ApprovalPolicyDto,
  type CreateOrUpdatePolicyPayload,
} from "@/services/workflow.service";

const POLICIES_QUERY_KEY = ["workflow", "policies"] as const;
const REQUEST_TYPES = ["Leave", "Advance", "Expense", "Overtime"] as const;
const MAX_STEPS = 6;

interface StepState {
  approverEmployeeId: string;
  isRequired: boolean;
}

export default function ApprovalFlowsPage() {
  const canRead = usePermission("workflow.policies.read");
  if (!canRead) return <NoAccess />;
  return <ApprovalFlows />;
}

function ApprovalFlows() {
  const { t } = useTranslation(["workflow", "common"]);
  const canCreate = usePermission("workflow.policies.create");
  const canUpdate = usePermission("workflow.policies.update");
  const canDeactivate = usePermission("workflow.policies.delete");

  const policiesQuery = useQuery({ queryKey: POLICIES_QUERY_KEY, queryFn: () => listPolicies() });
  const policies = useMemo(
    () =>
      [...(policiesQuery.data ?? [])].sort(
        (a, b) => a.requestType.localeCompare(b.requestType) || a.name.localeCompare(b.name),
      ),
    [policiesQuery.data],
  );

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const selected = !isCreating ? (policies.find((p) => p.id === selectedId) ?? policies[0]) : undefined;

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <WorkflowIcon size={24} /> {t("workflow:policies.listTitle")}
          </Title>
          <Text c="dimmed">{t("workflow:policies.listSubtitle")}</Text>
        </div>
        {canCreate && (
          <Button
            type="button"
            leftSection={<Plus size={16} />}
            onClick={() => {
              setIsCreating(true);
              setSelectedId(null);
            }}
          >
            {t("workflow:policies.addNew")}
          </Button>
        )}
      </Group>

      {policiesQuery.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[20rem_1fr]">
          <Skeleton h={256} />
          <Skeleton h={384} />
        </div>
      ) : policiesQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center">
            {getApiErrorMessage(policiesQuery.error, t("common:errorGeneric"))}
          </Text>
        </Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[20rem_1fr]">
          <Card withBorder padding="md">
            <Text fw={600} mb="sm">
              {t("workflow:policies.listTitle")}
            </Text>
            {/* The empty state is shown once, in the main pane; the list stays blank instead of repeating it (B7). */}
            {policies.length > 0 && (
              <Stack gap={4}>
                {policies.map((policy) => (
                  <NavLink
                    key={policy.id}
                    component="button"
                    type="button"
                    active={!isCreating && policy.id === selected?.id}
                    onClick={() => {
                      setIsCreating(false);
                      setSelectedId(policy.id);
                    }}
                    noWrap
                    label={policy.name}
                    description={t(`workflow:requestType.${policy.requestType}`)}
                    rightSection={
                      <Badge color={policy.isActive ? "green" : "gray"} variant="light">
                        {policy.isActive ? t("workflow:policies.active") : t("workflow:policies.inactive")}
                      </Badge>
                    }
                  />
                ))}
              </Stack>
            )}
          </Card>

          {isCreating || selected ? (
            <PolicyEditor
              key={isCreating ? "new" : selected!.id}
              policy={isCreating ? undefined : selected}
              readOnly={isCreating ? !canCreate : !canUpdate}
              canDeactivate={canDeactivate}
              onCancel={isCreating ? () => setIsCreating(false) : undefined}
              onSaved={(id) => {
                setIsCreating(false);
                setSelectedId(id);
              }}
            />
          ) : (
            <Card withBorder padding="lg">
              <Text size="sm" c="dimmed" ta="center">
                {t("workflow:policies.empty")}
              </Text>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}

interface PolicyEditorProps {
  /** Undefined = create mode. */
  policy?: ApprovalPolicyDto;
  readOnly: boolean;
  canDeactivate: boolean;
  onCancel?: () => void;
  onSaved: (id: string) => void;
}

function PolicyEditor({ policy, readOnly, canDeactivate, onCancel, onSaved }: PolicyEditorProps) {
  const { t } = useTranslation(["workflow", "common"]);
  const queryClient = useQueryClient();

  const [name, setName] = useState(policy?.name ?? "");
  const [requestType, setRequestType] = useState<string>(policy?.requestType ?? REQUEST_TYPES[0]);
  const [showCurrentApprover, setShowCurrentApprover] = useState(policy?.showCurrentApproverToRequester ?? false);
  const [steps, setSteps] = useState<StepState[]>(
    () => policy?.steps.map((s) => ({ approverEmployeeId: s.approverEmployeeId, isRequired: s.isRequired })) ?? [],
  );
  const [submitted, setSubmitted] = useState(false);
  const [deactivateOpen, setDeactivateOpen] = useState(false);

  const approverIds = useMemo(
    () => [...new Set(steps.map((s) => s.approverEmployeeId).filter(Boolean))],
    [steps],
  );
  const approverQueries = useQueries({
    queries: approverIds.map((id) => ({
      queryKey: ["workflow", "employee", id],
      queryFn: () => getEmployee(id),
      enabled: !!id,
      staleTime: 5 * 60_000,
    })),
  });
  const nameById = new Map<string, string>();
  approverIds.forEach((id, index) => {
    const displayName = approverQueries[index]?.data?.displayName;
    if (displayName) nameById.set(id, displayName);
  });

  function updateStep(index: number, patch: Partial<StepState>) {
    setSteps((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  }

  function addStep() {
    setSteps((prev) => (prev.length >= MAX_STEPS ? prev : [...prev, { approverEmployeeId: "", isRequired: true }]));
  }

  function removeStep(index: number) {
    setSteps((prev) => prev.filter((_, i) => i !== index));
  }

  function moveStep(index: number, direction: -1 | 1) {
    setSteps((prev) => {
      const target = index + direction;
      if (target < 0 || target >= prev.length) return prev;
      const next = [...prev];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  const mutation = useMutation({
    mutationFn: async (): Promise<string> => {
      const payload: CreateOrUpdatePolicyPayload = {
        requestType,
        name: name.trim(),
        showCurrentApproverToRequester: showCurrentApprover,
        steps: steps.map((s) => ({ approverEmployeeId: s.approverEmployeeId, isRequired: s.isRequired })),
      };
      if (policy) {
        await updatePolicy(policy.id, payload);
        return policy.id;
      }
      return createPolicy(payload);
    },
    onSuccess: (id) => {
      queryClient.invalidateQueries({ queryKey: POLICIES_QUERY_KEY });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(policy ? "workflow:policies.updateSuccess" : "workflow:policies.createSuccess"),
      });
      onSaved(id);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  const deactivateMutation = useMutation({
    mutationFn: () => deactivatePolicy(policy!.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: POLICIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("workflow:policies.deactivateSuccess") });
      setDeactivateOpen(false);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  const nameMissing = name.trim().length === 0;
  const stepsInvalid = steps.length === 0 || steps.some((s) => !s.approverEmployeeId);
  const canSubmit = !readOnly && !nameMissing && !stepsInvalid;

  const requestTypeOptions = REQUEST_TYPES.map((type) => ({ value: type, label: t(`workflow:requestType.${type}`) }));

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="sm" mb="md">
        <Text fw={600} size="lg">
          {policy ? t("workflow:policies.editTitle") : t("workflow:policies.createTitle")}
        </Text>
        {policy && policy.isActive && canDeactivate && (
          <Button type="button" variant="default" onClick={() => setDeactivateOpen(true)}>
            {t("workflow:policies.deactivate")}
          </Button>
        )}
      </Group>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (canSubmit) mutation.mutate();
        }}
      >
        <Stack gap="lg">
          <TextInput
            id="policy-name"
            autoFocus
            label={t("workflow:policies.name")}
            maxLength={200}
            placeholder={t("workflow:policies.namePlaceholder")}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            disabled={readOnly}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />

          <Select
            id="policy-request-type"
            label={t("workflow:policies.requestType")}
            placeholder={t("workflow:policies.selectRequestType")}
            data={requestTypeOptions}
            value={requestType}
            onChange={(value) => value && setRequestType(value)}
            allowDeselect={false}
            disabled={readOnly}
          />

          <Switch
            id="policy-show-current-approver"
            label={t("workflow:policies.showCurrentApproverToRequester")}
            checked={showCurrentApprover}
            onChange={(e) => setShowCurrentApprover(e.currentTarget.checked)}
            disabled={readOnly}
          />

          <Paper withBorder p="md">
            <Stack gap="sm">
              <Group justify="space-between">
                <Text size="sm" fw={600} component="h3">
                  {t("workflow:policies.steps.title")}
                </Text>
                {!readOnly && (
                  <Button
                    type="button"
                    variant="default"
                    size="xs"
                    leftSection={<Plus size={16} />}
                    onClick={addStep}
                    disabled={steps.length >= MAX_STEPS}
                  >
                    {t("workflow:policies.steps.addStep")}
                  </Button>
                )}
              </Group>

              {steps.length === 0 ? (
                <Text size="sm" c="dimmed">
                  {t("workflow:policies.steps.empty")}
                </Text>
              ) : (
                <Stack gap="sm">
                  {steps.map((step, index) => (
                    <Paper key={index} withBorder p="sm">
                      <Group gap="xs">
                        <Badge variant="outline" className="shrink-0">
                          {t("workflow:policies.steps.stepNumber", { number: index + 1 })}
                        </Badge>
                        <div className="min-w-[14rem] flex-1">
                          <EmployeePicker
                            value={
                              step.approverEmployeeId
                                ? { id: step.approverEmployeeId, displayName: nameById.get(step.approverEmployeeId) ?? step.approverEmployeeId }
                                : null
                            }
                            onChange={(picked: PickedEmployee | null) => updateStep(index, { approverEmployeeId: picked?.id ?? "" })}
                            placeholder={t("workflow:policies.steps.selectApprover")}
                            disabled={readOnly}
                          />
                        </div>
                        <Checkbox
                          id={`step-required-${index}`}
                          label={t("workflow:policies.steps.isRequired")}
                          checked={step.isRequired}
                          onChange={(e) => updateStep(index, { isRequired: e.currentTarget.checked })}
                          disabled={readOnly}
                          className="whitespace-nowrap"
                        />
                        {!readOnly && (
                          <Group gap={4} ml="auto" wrap="nowrap">
                            <Tooltip label={t("workflow:policies.steps.moveUp")}>
                              <ActionIcon
                                type="button"
                                variant="subtle"
                                color="gray"
                                aria-label={t("workflow:policies.steps.moveUp")}
                                onClick={() => moveStep(index, -1)}
                                disabled={index === 0}
                              >
                                <ArrowUp size={16} />
                              </ActionIcon>
                            </Tooltip>
                            <Tooltip label={t("workflow:policies.steps.moveDown")}>
                              <ActionIcon
                                type="button"
                                variant="subtle"
                                color="gray"
                                aria-label={t("workflow:policies.steps.moveDown")}
                                onClick={() => moveStep(index, 1)}
                                disabled={index === steps.length - 1}
                              >
                                <ArrowDown size={16} />
                              </ActionIcon>
                            </Tooltip>
                            <Tooltip label={t("workflow:policies.steps.removeStep")}>
                              <ActionIcon
                                type="button"
                                variant="subtle"
                                color="gray"
                                aria-label={t("workflow:policies.steps.removeStep")}
                                onClick={() => removeStep(index)}
                              >
                                <Trash2 size={16} />
                              </ActionIcon>
                            </Tooltip>
                          </Group>
                        )}
                      </Group>
                    </Paper>
                  ))}
                </Stack>
              )}
              {submitted && stepsInvalid && (
                <Text size="xs" c="red">
                  {t("common:required")}
                </Text>
              )}
            </Stack>
          </Paper>

          {!readOnly && (
            <Group justify="flex-end" gap="sm">
              {onCancel && (
                <Button type="button" variant="default" onClick={onCancel} disabled={mutation.isPending}>
                  {t("workflow:policies.cancel")}
                </Button>
              )}
              <Button type="submit" disabled={mutation.isPending}>
                {mutation.isPending ? t("common:saving") : t("workflow:policies.save")}
              </Button>
            </Group>
          )}
        </Stack>
      </form>

      {policy && (
        <ConfirmDialog
          open={deactivateOpen}
          onOpenChange={setDeactivateOpen}
          title={t("workflow:policies.deactivateTitle")}
          description={t("workflow:policies.deactivateConfirm", { name: policy.name })}
          confirmLabel={t("workflow:policies.deactivate")}
          isLoading={deactivateMutation.isPending}
          onConfirm={() => deactivateMutation.mutate()}
        />
      )}
    </Card>
  );
}
