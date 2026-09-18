import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Anchor, Button, Card, Group, Input, Menu, Modal, NumberInput, Select, Skeleton, Stack, Tabs, Text, Textarea, Title, Tooltip } from "@mantine/core";
import { Ban, Eye, ListChecks, ListOrdered, MoreVertical } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  adjustLeaveBalance,
  createLeaveRequestOnBehalf,
  getEmployeeLeaveBalances,
  getEmployeeLeaveRequest,
  getEmployeeLeaveRequests,
  getLeaveRuleSet,
  getMyLeaveBalances,
  getMyLeaveRequest,
  getMyLeaveRequests,
  previewEmployeeLeaveRequest,
  recalculateLeaveBalances,
  type LeaveAdjustmentInput,
  type LeaveRequest,
  type LeaveRequestStatus,
} from "@/services/leave.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import { LeaveBalanceSummary, LeaveLedgerDialog, LeaveRequestsTable, useLeaveName, useLeaveTypeNames } from "./leave-shared";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import { CancelLeaveRequestDialog, LeaveRequestDetailSheet } from "./leave-request-detail";
import { ChangeEmployeeRuleSetDialog } from "./employee-rule-set-dialog";
import { AbsencesPanel } from "./absences-panel";

export const LEAVE_CARD_TAB_PERMISSIONS = ["leave.balances.read", "leave.requests.read", "leave.my.balances", "leave.my.requests"];

const ALL_LEAVE_TYPES = "__all__";
const ALL_STATUSES = "__all__";
const LEAVE_STATUS_OPTIONS: LeaveRequestStatus[] = ["pending", "approved", "rejected", "withdrawn", "cancelled"];

/**
 * Employee card "İzinler": Özet (balances), Detaylar (ledger, leave.balances.read) and the request list.
 * Users without the scoped permissions see their own card through the `/leave/me` endpoints.
 */
export function LeaveSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const canReadBalances = usePermission("leave.balances.read");
  const canReadRequests = usePermission("leave.requests.read");
  const canReadRuleSets = usePermission("leave.rule_sets.read");
  const canMyBalances = usePermission("leave.my.balances");
  const canMyRequests = usePermission("leave.my.requests");
  const isOwn = useAuthStore((state) => state.user?.employeeId) === employee.id;
  const [ledgerOpen, setLedgerOpen] = useState(false);
  const [adjustOpen, setAdjustOpen] = useState(false);
  const [recalculateOpen, setRecalculateOpen] = useState(false);
  const canAdjust = usePermission("leave.balances.adjust");
  const canRecalculate = usePermission("leave.balances.recalculate");
  const canAddLeave = usePermission("leave.requests.create_on_behalf");
  const canBypassApproval = usePermission("leave.requests.bypass_approval");
  const [addOpen, setAddOpen] = useState(false);
  const canAssignRuleSet = usePermission("leave.rule_sets.assign");
  const canCancel = usePermission("leave.requests.cancel_approved");
  const canViewAbsences = usePermission("leave.absences.view");
  const [ruleSetOpen, setRuleSetOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState<LeaveRequest | null>(null);
  const [typeFilter, setTypeFilter] = useState(ALL_LEAVE_TYPES);
  const [statusFilter, setStatusFilter] = useState(ALL_STATUSES);
  const queryClient = useQueryClient();

  const balancesMode = canReadBalances ? "employee" : isOwn && canMyBalances ? "me" : null;
  const requestsMode = canReadRequests ? "employee" : isOwn && canMyRequests ? "me" : null;

  const balancesQuery = useQuery({
    queryKey: ["leave", "balances", balancesMode === "me" ? "me" : employee.id],
    queryFn: () => (balancesMode === "me" ? getMyLeaveBalances() : getEmployeeLeaveBalances(employee.id)),
    enabled: balancesMode !== null,
    retry: false,
  });
  const ruleSetId = balancesQuery.data?.ruleSetId;
  const ruleSetQuery = useQuery({
    queryKey: ["leave", "rule-sets", ruleSetId],
    queryFn: () => getLeaveRuleSet(ruleSetId!),
    enabled: canReadRuleSets && !!ruleSetId,
  });
  const requestsQuery = useQuery({
    queryKey: ["leave", "requests", requestsMode === "me" ? "me" : employee.id],
    queryFn: () => (requestsMode === "me" ? getMyLeaveRequests() : getEmployeeLeaveRequests(employee.id)),
    enabled: requestsMode !== null,
  });

  const typeName = useLeaveTypeNames(balancesQuery.data, ruleSetQuery.data);
  const ledgerTypes = useMemo(() => {
    if (ruleSetQuery.data) return ruleSetQuery.data.types.map((type) => ({ id: type.id, name: leaveName(type.settings.name) }));
    return (balancesQuery.data?.balances ?? []).map((item) => ({ id: item.balance.leaveTypeId, name: leaveName(item.leaveTypeName) }));
  }, [ruleSetQuery.data, balancesQuery.data, leaveName]);
  // İzin Ekle: every active type of the rule set (self-service flag does not apply on behalf); balance types when the rule set is not readable.
  const addLeaveTypes = useMemo<LeaveRequestTypeOption[]>(() => {
    const available = new Map((balancesQuery.data?.balances ?? []).map((item) => [item.balance.leaveTypeId, item.balance.availableBalance]));
    if (ruleSetQuery.data) {
      return [...ruleSetQuery.data.types]
        .filter((type) => !type.settings.isDisabled)
        .sort((a, b) => a.sortOrder - b.sortOrder)
        .map((type) => ({
          id: type.id,
          name: leaveName(type.settings.name),
          description: type.settings.description ?? null,
          requiresDescription: type.settings.requiresDescription,
          perRequestMaxDays: type.settings.perRequestMaxDays ?? null,
          availableBalance: available.get(type.id) ?? null,
          unit: type.settings.unit,
        }));
    }
    // Rule set not readable (leave.rule_sets.read missing): balance types only, unit unknown → FullDay-only form.
    return (balancesQuery.data?.balances ?? []).map((item) => ({
      id: item.balance.leaveTypeId,
      name: leaveName(item.leaveTypeName),
      description: null,
      requiresDescription: false,
      perRequestMaxDays: null,
      availableBalance: item.balance.availableBalance,
    }));
  }, [ruleSetQuery.data, balancesQuery.data, leaveName]);

  const recalculateMutation = useMutation({
    mutationFn: () => recalculateLeaveBalances(employee.id),
    onSuccess: () => {
      setRecalculateOpen(false);
      toast({ variant: "success", title: t("common:success"), description: t("leave:actions.recalculated") });
      void queryClient.invalidateQueries({ queryKey: ["leave"] });
    },
    onError: (error) => {
      setRecalculateOpen(false);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) });
    },
  });

  if (balancesMode === null && requestsMode === null && !canViewAbsences) {
    return (
      <Text size="sm" c="dimmed">
        {t("leave:noAccess")}
      </Text>
    );
  }

  // Cancelling is an employee-scoped action: only offered through the scoped requests list.
  const cancellable = (request: LeaveRequest) =>
    requestsMode === "employee" && canCancel && (request.status === "pending" || request.status === "approved");
  const canChangeRuleSet = balancesMode === "employee" && canAssignRuleSet && !!ruleSetId;

  const leavePanel = (
    <>
      {balancesMode !== null && (
        <LeaveBalanceSummary
          data={balancesQuery.data}
          isLoading={balancesQuery.isLoading}
          emptyMessage={balancesQuery.isError ? getApiErrorMessage(balancesQuery.error, t("leave:summary.unavailable")) : undefined}
          ruleSet={
            ruleSetQuery.data || canChangeRuleSet ? (
              <span className="flex flex-wrap items-center gap-2">
                <span>{ruleSetQuery.data ? leaveName(ruleSetQuery.data.name) : "—"}</span>
                {canChangeRuleSet && (
                  <Anchor component="button" type="button" size="sm" onClick={() => setRuleSetOpen(true)}>
                    {t("leave:ruleSetChange.action")}
                  </Anchor>
                )}
              </span>
            ) : undefined
          }
          action={
            balancesQuery.data ? (
              <Group gap="xs">
                <Button type="button" size="sm" variant="default" leftSection={<ListOrdered size={16} />} onClick={() => setLedgerOpen(true)}>
                  {t("leave:summary.details")}
                </Button>
                {canReadBalances && (canAdjust || canRecalculate || canAddLeave) && (
                  <Menu position="bottom-end">
                    <Menu.Target>
                      <Button type="button" size="sm" variant="default" rightSection={<MoreVertical size={16} />}>
                        {t("leave:actions.menu")}
                      </Button>
                    </Menu.Target>
                    <Menu.Dropdown>
                      {canAddLeave && <Menu.Item onClick={() => setAddOpen(true)}>{t("leave:actions.addLeave")}</Menu.Item>}
                      {canAdjust && balancesQuery.data.balances.length > 0 && (
                        <Menu.Item onClick={() => setAdjustOpen(true)}>{t("leave:actions.adjust")}</Menu.Item>
                      )}
                      {canRecalculate && <Menu.Item onClick={() => setRecalculateOpen(true)}>{t("leave:actions.recalculate")}</Menu.Item>}
                    </Menu.Dropdown>
                  </Menu>
                )}
              </Group>
            ) : undefined
          }
        />
      )}

      {requestsMode !== null && (
        <Card withBorder padding="lg">
          <Group justify="space-between" wrap="wrap" mb="md" gap="sm">
            <Group gap="xs">
              <ListChecks size={16} color="var(--mantine-color-dimmed)" />
              <Title order={4}>{t("leave:requests.title")}</Title>
            </Group>
            <Group gap="sm" wrap="wrap">
              <Select
                id="leave-requests-type-filter"
                aria-label={t("leave:requests.filterType")}
                placeholder={t("leave:requests.filterType")}
                data={[{ value: ALL_LEAVE_TYPES, label: t("leave:requests.allTypes") }, ...ledgerTypes.map((type) => ({ value: type.id, label: type.name }))]}
                value={typeFilter}
                onChange={(value) => setTypeFilter(value ?? ALL_LEAVE_TYPES)}
                allowDeselect={false}
                w={200}
              />
              <Select
                id="leave-requests-status-filter"
                aria-label={t("leave:requests.filterStatus")}
                placeholder={t("leave:requests.filterStatus")}
                data={[
                  { value: ALL_STATUSES, label: t("leave:requests.allStatuses") },
                  ...LEAVE_STATUS_OPTIONS.map((status) => ({ value: status, label: t(`leave:status.${status}`) })),
                ]}
                value={statusFilter}
                onChange={(value) => setStatusFilter(value ?? ALL_STATUSES)}
                allowDeselect={false}
                w={180}
              />
            </Group>
          </Group>
          {requestsQuery.isLoading ? (
            <Skeleton h={128} />
          ) : requestsQuery.isError ? (
            <Text size="sm" c="red">
              {getApiErrorMessage(requestsQuery.error, t("leave:genericError"))}
            </Text>
          ) : (
            <LeaveRequestsTable
              requests={(requestsQuery.data ?? []).filter(
                (request) =>
                  (typeFilter === ALL_LEAVE_TYPES || request.leaveTypeId === typeFilter) &&
                  (statusFilter === ALL_STATUSES || request.status === statusFilter)
              )}
              typeName={typeName}
              onOpen={(request) => setDetailId(request.id)}
              actions={(request) => (
                <Group justify="flex-end" gap={4} wrap="nowrap">
                  <Tooltip label={t("leave:detail.open")}>
                    <ActionIcon type="button" variant="subtle" color="gray" aria-label={t("leave:detail.open")} onClick={() => setDetailId(request.id)}>
                      <Eye size={16} />
                    </ActionIcon>
                  </Tooltip>
                  {cancellable(request) && (
                    <Tooltip label={t("leave:cancel.action")}>
                      <ActionIcon type="button" variant="subtle" color="gray" aria-label={t("leave:cancel.action")} onClick={() => setCancelling(request)}>
                        <Ban size={16} />
                      </ActionIcon>
                    </Tooltip>
                  )}
                </Group>
              )}
            />
          )}
        </Card>
      )}
    </>
  );

  return (
    <Stack gap="lg">
      {canViewAbsences ? (
        <Tabs defaultValue="leave" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="leave">{t("leave:tabs.leave")}</Tabs.Tab>
            <Tabs.Tab value="absences">{t("leave:tabs.absences")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="leave" pt="md">
            <Stack gap="lg">{leavePanel}</Stack>
          </Tabs.Panel>
          <Tabs.Panel value="absences" pt="md">
            <AbsencesPanel employeeId={employee.id} />
          </Tabs.Panel>
        </Tabs>
      ) : (
        leavePanel
      )}

      {detailId && requestsMode !== null && (
        <LeaveRequestDetailSheet
          requestId={detailId}
          scope={requestsMode === "me" ? "me" : employee.id}
          load={(id) => (requestsMode === "me" ? getMyLeaveRequest(id) : getEmployeeLeaveRequest(employee.id, id))}
          typeName={typeName}
          onClose={() => setDetailId(null)}
          footer={(detail) =>
            cancellable(detail) ? (
              <Button
                type="button"
                color="red"
                onClick={() => {
                  setDetailId(null);
                  setCancelling(detail);
                }}
              >
                {t("leave:cancel.action")}
              </Button>
            ) : null
          }
        />
      )}

      {cancelling && (
        <CancelLeaveRequestDialog
          employeeId={employee.id}
          request={cancelling}
          typeName={typeName}
          onClose={() => setCancelling(null)}
          onCancelled={() => {
            setCancelling(null);
            toast({ variant: "success", title: t("common:success"), description: t("leave:cancel.done") });
            void queryClient.invalidateQueries({ queryKey: ["leave"] });
          }}
        />
      )}

      {ruleSetOpen && ruleSetId && (
        <ChangeEmployeeRuleSetDialog
          employeeId={employee.id}
          employeeName={employee.displayName}
          currentRuleSetId={ruleSetId}
          onClose={() => setRuleSetOpen(false)}
          onChanged={(result) => {
            setRuleSetOpen(false);
            toast({
              variant: "success",
              title: t("common:success"),
              description: [t("leave:ruleSetChange.done"), ...result.warnings.map((warning) => warning.message)].join(" "),
            });
            void queryClient.invalidateQueries({ queryKey: ["leave"] });
          }}
        />
      )}

      {ledgerOpen && (
        <LeaveLedgerDialog employeeId={employee.id} own={balancesMode === "me"} typeOptions={ledgerTypes} onClose={() => setLedgerOpen(false)} />
      )}

      {addOpen && (
        <LeaveRequestDialog
          editing={null}
          types={addLeaveTypes}
          title={t("leave:form.onBehalfTitle")}
          description={t("leave:form.onBehalfDescription")}
          submitLabel={t("common:save")}
          typesHint={ruleSetQuery.data ? undefined : t("leave:form.typesLimited")}
          previewScope={employee.id}
          preview={(input) => previewEmployeeLeaveRequest(employee.id, input)}
          save={(input, bypassApproval) => createLeaveRequestOnBehalf(employee.id, { ...input, bypassApproval })}
          allowBypass={canBypassApproval}
          onClose={() => setAddOpen(false)}
          onSaved={() => {
            setAddOpen(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:form.onBehalfSaved") });
            void queryClient.invalidateQueries({ queryKey: ["leave"] });
          }}
        />
      )}

      {adjustOpen && balancesQuery.data && (
        <AdjustBalanceDialog
          employeeId={employee.id}
          typeOptions={balancesQuery.data.balances.map((item) => ({ id: item.balance.leaveTypeId, name: leaveName(item.leaveTypeName) }))}
          onClose={() => setAdjustOpen(false)}
          onSaved={() => {
            setAdjustOpen(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:actions.adjusted") });
            void queryClient.invalidateQueries({ queryKey: ["leave"] });
          }}
        />
      )}

      <ConfirmDialog
        open={recalculateOpen}
        onOpenChange={setRecalculateOpen}
        title={t("leave:actions.recalculateTitle")}
        description={t("leave:actions.recalculateDescription")}
        confirmLabel={t("leave:actions.recalculate")}
        confirmVariant="default"
        isLoading={recalculateMutation.isPending}
        onConfirm={() => recalculateMutation.mutate()}
      />
    </Stack>
  );
}

/** Today as a backend DateOnly in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

interface AdjustBalanceDialogProps {
  employeeId: string;
  typeOptions: { id: string; name: string }[];
  onClose: () => void;
  onSaved: () => void;
}

/** "Bakiye düzeltmesi": signed Adjustment ledger row with a mandatory reason (PerYear types only). */
function AdjustBalanceDialog({ employeeId, typeOptions, onClose, onSaved }: AdjustBalanceDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const [leaveTypeId, setLeaveTypeId] = useState(typeOptions.length === 1 ? typeOptions[0].id : "");
  const [days, setDays] = useState("");
  const [effectiveDate, setEffectiveDate] = useState(todayDateOnly());
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: LeaveAdjustmentInput) => adjustLeaveBalance(employeeId, input),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    const parsed = Number(days);
    if (!leaveTypeId) return setError(t("leave:actions.validation.typeRequired"));
    if (days.trim() === "" || !Number.isFinite(parsed) || parsed === 0) return setError(t("leave:actions.validation.daysInvalid"));
    if (!effectiveDate) return setError(t("leave:actions.validation.dateRequired"));
    if (!reason.trim()) return setError(t("leave:actions.validation.reasonRequired"));
    setError(null);
    mutation.mutate({ leaveTypeId, days: parsed, effectiveDate, reason: reason.trim() });
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} size="32rem" title={t("leave:actions.adjust")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:actions.adjustDescription")}
          </Text>
          <Select
            id="adjust-type"
            label={t("leave:actions.type")}
            withAsterisk
            placeholder={t("leave:actions.type")}
            value={leaveTypeId || null}
            onChange={(value) => setLeaveTypeId(value ?? "")}
            allowDeselect={false}
            disabled={busy}
            data={typeOptions.map((option) => ({ value: option.id, label: option.name }))}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <NumberInput
              id="adjust-days"
              label={t("leave:actions.days")}
              withAsterisk
              inputMode="decimal"
              step={0.5}
              decimalScale={2}
              value={days}
              disabled={busy}
              onChange={(value) => setDays(String(value))}
            />
            <Input.Wrapper id="adjust-date" label={t("leave:actions.effectiveDate")} withAsterisk>
              <DatePicker id="adjust-date" value={effectiveDate} disabled={busy} onChange={setEffectiveDate} />
            </Input.Wrapper>
          </div>
          <Textarea
            id="adjust-reason"
            label={t("leave:actions.reason")}
            withAsterisk
            value={reason}
            maxLength={1000}
            disabled={busy}
            autosize
            minRows={3}
            onChange={(e) => setReason(e.currentTarget.value)}
          />
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
    </Modal>
  );
}
