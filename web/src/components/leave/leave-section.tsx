import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, Eye, ListChecks, ListOrdered, MoreVertical } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
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
} from "@/services/leave.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import { LeaveBalanceSummary, LeaveLedgerDialog, LeaveRequestsTable, useLeaveName, useLeaveTypeNames } from "./leave-shared";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import { CancelLeaveRequestDialog, LeaveRequestDetailSheet } from "./leave-request-detail";
import { ChangeEmployeeRuleSetDialog } from "./employee-rule-set-dialog";

export const LEAVE_CARD_TAB_PERMISSIONS = ["leave.balances.read", "leave.requests.read", "leave.my.balances", "leave.my.requests"];

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
  const [ruleSetOpen, setRuleSetOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState<LeaveRequest | null>(null);
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
        }));
    }
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

  if (balancesMode === null && requestsMode === null) {
    return <p className="text-sm text-muted-foreground">{t("leave:noAccess")}</p>;
  }

  // Cancelling is an employee-scoped action: only offered through the scoped requests list.
  const cancellable = (request: LeaveRequest) =>
    requestsMode === "employee" && canCancel && (request.status === "pending" || request.status === "approved");
  const canChangeRuleSet = balancesMode === "employee" && canAssignRuleSet && !!ruleSetId;

  return (
    <div className="space-y-6">
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
                  <Button type="button" size="sm" variant="link" className="h-auto p-0" onClick={() => setRuleSetOpen(true)}>
                    {t("leave:ruleSetChange.action")}
                  </Button>
                )}
              </span>
            ) : undefined
          }
          action={
            balancesQuery.data ? (
              <div className="flex items-center gap-2">
                <Button type="button" size="sm" variant="outline" onClick={() => setLedgerOpen(true)}>
                  <ListOrdered className="mr-2 h-4 w-4" /> {t("leave:summary.details")}
                </Button>
                {canReadBalances && (canAdjust || canRecalculate || canAddLeave) && (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button type="button" size="sm" variant="outline">
                        {t("leave:actions.menu")} <MoreVertical className="ml-1 h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      {canAddLeave && <DropdownMenuItem onSelect={() => setAddOpen(true)}>{t("leave:actions.addLeave")}</DropdownMenuItem>}
                      {canAdjust && balancesQuery.data.balances.length > 0 && (
                        <DropdownMenuItem onSelect={() => setAdjustOpen(true)}>{t("leave:actions.adjust")}</DropdownMenuItem>
                      )}
                      {canRecalculate && (
                        <DropdownMenuItem onSelect={() => setRecalculateOpen(true)}>{t("leave:actions.recalculate")}</DropdownMenuItem>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                )}
              </div>
            ) : undefined
          }
        />
      )}

      {requestsMode !== null && (
        <Card>
          <CardHeader className="flex flex-row items-center gap-2 space-y-0 pb-3">
            <ListChecks className="h-4 w-4 text-muted-foreground" />
            <CardTitle className="text-lg">{t("leave:requests.title")}</CardTitle>
          </CardHeader>
          <CardContent>
            {requestsQuery.isLoading ? (
              <Skeleton className="h-32 w-full" />
            ) : requestsQuery.isError ? (
              <p className="text-sm text-destructive">{getApiErrorMessage(requestsQuery.error, t("leave:genericError"))}</p>
            ) : (
              <LeaveRequestsTable
                requests={requestsQuery.data ?? []}
                typeName={typeName}
                onOpen={(request) => setDetailId(request.id)}
                actions={(request) => (
                  <div className="flex justify-end gap-1">
                    <Button type="button" variant="ghost" size="icon" title={t("leave:detail.open")} onClick={() => setDetailId(request.id)}>
                      <Eye className="h-4 w-4" />
                    </Button>
                    {cancellable(request) && (
                      <Button type="button" variant="ghost" size="icon" title={t("leave:cancel.action")} onClick={() => setCancelling(request)}>
                        <Ban className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                )}
              />
            )}
          </CardContent>
        </Card>
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
                variant="destructive"
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
    </div>
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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("leave:actions.adjust")}</DialogTitle>
          <DialogDescription>{t("leave:actions.adjustDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="adjust-type">
              {t("leave:actions.type")}
              <span className="text-destructive"> *</span>
            </Label>
            <Select value={leaveTypeId} onValueChange={setLeaveTypeId} disabled={busy}>
              <SelectTrigger id="adjust-type">
                <SelectValue placeholder={t("leave:actions.type")} />
              </SelectTrigger>
              <SelectContent>
                {typeOptions.map((option) => (
                  <SelectItem key={option.id} value={option.id}>
                    {option.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="adjust-days">
                {t("leave:actions.days")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input id="adjust-days" type="number" inputMode="decimal" step="0.5" value={days} disabled={busy} onChange={(e) => setDays(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="adjust-date">
                {t("leave:actions.effectiveDate")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input id="adjust-date" type="date" value={effectiveDate} disabled={busy} onChange={(e) => setEffectiveDate(e.target.value)} />
            </div>
          </div>
          <div>
            <Label htmlFor="adjust-reason">
              {t("leave:actions.reason")}
              <span className="text-destructive"> *</span>
            </Label>
            <Textarea id="adjust-reason" value={reason} maxLength={1000} disabled={busy} onChange={(e) => setReason(e.target.value)} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
