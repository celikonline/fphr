import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Plus, Trash2, Workflow as WorkflowIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { cn } from "@/lib/utils";
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
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <WorkflowIcon className="h-6 w-6" /> {t("workflow:policies.listTitle")}
          </h1>
          <p className="text-muted-foreground">{t("workflow:policies.listSubtitle")}</p>
        </div>
        {canCreate && (
          <Button
            type="button"
            onClick={() => {
              setIsCreating(true);
              setSelectedId(null);
            }}
          >
            <Plus className="mr-2 h-4 w-4" /> {t("workflow:policies.addNew")}
          </Button>
        )}
      </div>

      {policiesQuery.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[20rem_1fr]">
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-96 w-full" />
        </div>
      ) : policiesQuery.isError ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-destructive">
            {getApiErrorMessage(policiesQuery.error, t("common:errorGeneric"))}
          </CardContent>
        </Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[20rem_1fr]">
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">{t("workflow:policies.listTitle")}</CardTitle>
            </CardHeader>
            <CardContent className="space-y-1 px-3 pb-4">
              {policies.length === 0 ? (
                <p className="p-3 text-sm text-muted-foreground">{t("workflow:policies.empty")}</p>
              ) : (
                policies.map((policy) => (
                  <button
                    key={policy.id}
                    type="button"
                    onClick={() => {
                      setIsCreating(false);
                      setSelectedId(policy.id);
                    }}
                    className={cn(
                      "flex w-full flex-col items-start gap-0.5 rounded-md border px-3 py-2 text-left text-sm transition-colors",
                      !isCreating && policy.id === selected?.id
                        ? "border-primary bg-primary/5 font-semibold"
                        : "border-transparent hover:bg-muted",
                    )}
                  >
                    <span className="text-xs text-muted-foreground">{t(`workflow:requestType.${policy.requestType}`)}</span>
                    <span className="flex w-full items-center gap-2">
                      <span className="min-w-0 flex-1 truncate">{policy.name}</span>
                      <Badge variant={policy.isActive ? "success" : "secondary"}>
                        {policy.isActive ? t("workflow:policies.active") : t("workflow:policies.inactive")}
                      </Badge>
                    </span>
                  </button>
                ))
              )}
            </CardContent>
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
            <Card>
              <CardContent className="p-6 text-center text-sm text-muted-foreground">{t("workflow:policies.empty")}</CardContent>
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

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3 space-y-0 pb-4">
        <CardTitle className="text-lg">
          {policy ? t("workflow:policies.editTitle") : t("workflow:policies.createTitle")}
        </CardTitle>
        {policy && policy.isActive && canDeactivate && (
          <Button type="button" variant="outline" onClick={() => setDeactivateOpen(true)}>
            {t("workflow:policies.deactivate")}
          </Button>
        )}
      </CardHeader>
      <CardContent>
        <form
          className="space-y-5"
          onSubmit={(e) => {
            e.preventDefault();
            setSubmitted(true);
            if (canSubmit) mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="policy-name">{t("workflow:policies.name")}</Label>
            <Input
              id="policy-name"
              autoFocus
              maxLength={200}
              placeholder={t("workflow:policies.namePlaceholder")}
              value={name}
              onChange={(e) => setName(e.target.value)}
              disabled={readOnly}
            />
            {submitted && nameMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
          </div>

          <div>
            <Label htmlFor="policy-request-type">{t("workflow:policies.requestType")}</Label>
            <Select value={requestType} onValueChange={setRequestType} disabled={readOnly}>
              <SelectTrigger id="policy-request-type">
                <SelectValue placeholder={t("workflow:policies.selectRequestType")} />
              </SelectTrigger>
              <SelectContent>
                {REQUEST_TYPES.map((type) => (
                  <SelectItem key={type} value={type}>
                    {t(`workflow:requestType.${type}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center gap-2">
            <Switch
              id="policy-show-current-approver"
              checked={showCurrentApprover}
              onCheckedChange={setShowCurrentApprover}
              disabled={readOnly}
            />
            <Label htmlFor="policy-show-current-approver" className="font-normal">
              {t("workflow:policies.showCurrentApproverToRequester")}
            </Label>
          </div>

          <div className="space-y-3 rounded-md border p-4">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold">{t("workflow:policies.steps.title")}</h3>
              {!readOnly && (
                <Button type="button" variant="outline" size="sm" onClick={addStep} disabled={steps.length >= MAX_STEPS}>
                  <Plus className="mr-1 h-4 w-4" /> {t("workflow:policies.steps.addStep")}
                </Button>
              )}
            </div>

            {steps.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("workflow:policies.steps.empty")}</p>
            ) : (
              <div className="space-y-3">
                {steps.map((step, index) => (
                  <div key={index} className="flex flex-wrap items-center gap-2 rounded-md border p-3">
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
                    <div className="flex items-center gap-2">
                      <Checkbox
                        id={`step-required-${index}`}
                        checked={step.isRequired}
                        onCheckedChange={(checked) => updateStep(index, { isRequired: checked === true })}
                        disabled={readOnly}
                      />
                      <Label htmlFor={`step-required-${index}`} className="whitespace-nowrap font-normal">
                        {t("workflow:policies.steps.isRequired")}
                      </Label>
                    </div>
                    {!readOnly && (
                      <div className="ml-auto flex items-center gap-1">
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => moveStep(index, -1)}
                          disabled={index === 0}
                        >
                          <ArrowUp className="h-4 w-4" />
                        </Button>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => moveStep(index, 1)}
                          disabled={index === steps.length - 1}
                        >
                          <ArrowDown className="h-4 w-4" />
                        </Button>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          title={t("workflow:policies.steps.removeStep")}
                          onClick={() => removeStep(index)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
            {submitted && stepsInvalid && <p className="text-xs text-destructive">{t("common:required")}</p>}
          </div>

          {!readOnly && (
            <div className="flex justify-end gap-2">
              {onCancel && (
                <Button type="button" variant="outline" onClick={onCancel} disabled={mutation.isPending}>
                  {t("workflow:policies.cancel")}
                </Button>
              )}
              <Button type="submit" disabled={mutation.isPending}>
                {mutation.isPending ? t("common:saving") : t("workflow:policies.save")}
              </Button>
            </div>
          )}
        </form>
      </CardContent>

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
