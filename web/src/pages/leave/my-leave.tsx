import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarDays, ListChecks, Pencil, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { LeaveBalanceSummary, LeaveRequestsTable, useDays, useLeaveName, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  getLeaveRuleSet,
  getMyLeaveBalances,
  getMyLeaveRequests,
  submitLeaveRequest,
  updatePendingLeaveRequest,
  withdrawLeaveRequest,
  type EmployeeLeaveBalances,
  type LeaveRequest,
  type LeaveRequestInput,
  type LeaveType,
} from "@/services/leave.service";

const MY_LEAVE_KEY = ["leave", "me"] as const;

interface RequestableType {
  id: string;
  name: string;
  description: string | null;
  requiresDescription: boolean;
  perRequestMaxDays: number | null;
  availableBalance: number | null;
}

export default function MyLeavePage() {
  const canBalances = usePermission("leave.my.balances");
  const canRequests = usePermission("leave.my.requests");
  if (!canBalances && !canRequests) return <NoAccess />;
  return <MyLeave canBalances={canBalances} canRequests={canRequests} />;
}

function MyLeave({ canBalances, canRequests }: { canBalances: boolean; canRequests: boolean }) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const queryClient = useQueryClient();
  const leaveName = useLeaveName();
  const canReadRuleSets = usePermission("leave.rule_sets.read");
  const [dialog, setDialog] = useState<{ mode: "add" } | { mode: "edit"; request: LeaveRequest } | null>(null);
  const [withdrawing, setWithdrawing] = useState<LeaveRequest | null>(null);

  const balancesQuery = useQuery({
    queryKey: [...MY_LEAVE_KEY, "balances"],
    queryFn: getMyLeaveBalances,
    enabled: canBalances,
    retry: false,
  });
  const ruleSetId = balancesQuery.data?.ruleSetId;
  const ruleSetQuery = useQuery({
    queryKey: ["leave", "rule-sets", ruleSetId],
    queryFn: () => getLeaveRuleSet(ruleSetId!),
    enabled: canReadRuleSets && !!ruleSetId,
  });
  const requestsQuery = useQuery({ queryKey: [...MY_LEAVE_KEY, "requests"], queryFn: getMyLeaveRequests, enabled: canRequests });

  const typeName = useLeaveTypeNames(balancesQuery.data, ruleSetQuery.data);
  const requestableTypes = useMemo(
    () => buildRequestableTypes(balancesQuery.data, ruleSetQuery.data?.types, leaveName),
    [balancesQuery.data, ruleSetQuery.data, leaveName],
  );

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["leave"] });

  const withdrawMutation = useMutation({
    mutationFn: (request: LeaveRequest) => withdrawLeaveRequest(request.id),
    onSuccess: () => {
      setWithdrawing(null);
      toast({ variant: "success", title: t("common:success"), description: t("leave:my.withdrawn") });
      void refresh();
    },
    onError: (error) => {
      setWithdrawing(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) });
    },
  });

  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <CalendarDays className="h-6 w-6" /> {t("leave:my.title")}
          </h1>
          <p className="text-muted-foreground">{t("leave:my.subtitle")}</p>
        </div>
        {canRequests && (
          <Button type="button" onClick={() => setDialog({ mode: "add" })}>
            {t("leave:my.newRequest")}
          </Button>
        )}
      </div>

      {canBalances && (
        <LeaveBalanceSummary
          data={balancesQuery.data}
          isLoading={balancesQuery.isLoading}
          emptyMessage={balancesQuery.isError ? getApiErrorMessage(balancesQuery.error, t("leave:summary.unavailable")) : undefined}
        />
      )}

      {canRequests && (
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
                actions={(request) =>
                  request.status === "pending" ? (
                    <div className="flex justify-end gap-1">
                      <Button type="button" variant="ghost" size="icon" title={t("leave:my.edit")} onClick={() => setDialog({ mode: "edit", request })}>
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button type="button" variant="ghost" size="icon" title={t("leave:my.withdraw")} onClick={() => setWithdrawing(request)}>
                        <Undo2 className="h-4 w-4" />
                      </Button>
                    </div>
                  ) : null
                }
              />
            )}
          </CardContent>
        </Card>
      )}

      {dialog && (
        <LeaveRequestDialog
          key={dialog.mode === "edit" ? dialog.request.id : "add"}
          editing={dialog.mode === "edit" ? dialog.request : null}
          types={requestableTypes}
          typesLimited={!ruleSetQuery.data}
          onClose={() => setDialog(null)}
          onSaved={(message) => {
            setDialog(null);
            toast({ variant: "success", title: t("common:success"), description: message });
            void refresh();
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(withdrawing)}
        onOpenChange={(open) => !open && setWithdrawing(null)}
        title={t("leave:my.withdrawTitle")}
        description={t("leave:my.withdrawDescription", {
          type: withdrawing ? typeName(withdrawing) : "",
          start: withdrawing ? date(withdrawing.startDate) : "",
          end: withdrawing ? date(withdrawing.endDate) : "",
        })}
        confirmLabel={t("leave:my.withdraw")}
        isLoading={withdrawMutation.isPending}
        onConfirm={() => withdrawing && withdrawMutation.mutate(withdrawing)}
      />
    </div>
  );
}

/** Self-service types: the whole rule set when readable (active + self-service types), otherwise only the PerYear types in the balances. */
function buildRequestableTypes(
  balances: EmployeeLeaveBalances | undefined,
  ruleSetTypes: LeaveType[] | undefined,
  leaveName: (name: string) => string,
): RequestableType[] {
  const available = new Map((balances?.balances ?? []).map((item) => [item.balance.leaveTypeId, item.balance.availableBalance]));
  if (ruleSetTypes) {
    return [...ruleSetTypes]
      .filter((type) => !type.settings.isDisabled && type.settings.allowSelfServiceRequest)
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
  return (balances?.balances ?? []).map((item) => ({
    id: item.balance.leaveTypeId,
    name: leaveName(item.leaveTypeName),
    description: null,
    requiresDescription: false,
    perRequestMaxDays: null,
    availableBalance: item.balance.availableBalance,
  }));
}

interface LeaveRequestDialogProps {
  editing: LeaveRequest | null;
  types: RequestableType[];
  typesLimited: boolean;
  onClose: () => void;
  onSaved: (message: string) => void;
}

function LeaveRequestDialog({ editing, types, typesLimited, onClose, onSaved }: LeaveRequestDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const days = useDays();
  const [leaveTypeId, setLeaveTypeId] = useState(editing?.leaveTypeId ?? (types.length === 1 ? types[0].id : ""));
  const [startDate, setStartDate] = useState(editing?.startDate ?? "");
  const [endDate, setEndDate] = useState(editing?.endDate ?? "");
  const [description, setDescription] = useState(editing?.description ?? "");
  const [error, setError] = useState<string | null>(null);

  // A pending request's type may be missing from the list (e.g. no rule set read access); keep it selectable.
  const options = editing && !types.some((type) => type.id === editing.leaveTypeId)
    ? [...types, { id: editing.leaveTypeId, name: t(`leave:code.${editing.leaveTypeCode}`), description: null, requiresDescription: false, perRequestMaxDays: null, availableBalance: null }]
    : types;
  const selected = options.find((type) => type.id === leaveTypeId) ?? null;

  const mutation = useMutation({
    mutationFn: (input: LeaveRequestInput) => (editing ? updatePendingLeaveRequest(editing.id, input) : submitLeaveRequest(input).then(() => undefined)),
    onSuccess: () => onSaved(editing ? t("leave:my.updated") : t("leave:my.submitted")),
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!leaveTypeId) return setError(t("leave:form.validation.typeRequired"));
    if (!startDate || !endDate) return setError(t("leave:form.validation.datesRequired"));
    if (endDate < startDate) return setError(t("leave:form.validation.range"));
    if (selected?.requiresDescription && !description.trim()) return setError(t("leave:form.validation.descriptionRequired"));
    setError(null);
    mutation.mutate({ leaveTypeId, startDate, endDate, description: description.trim() || null });
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{editing ? t("leave:form.editTitle") : t("leave:form.newTitle")}</DialogTitle>
          <DialogDescription>{t("leave:form.dialogDescription")}</DialogDescription>
        </DialogHeader>
        {options.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:form.noTypes")}</p>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <div>
              <Label htmlFor="leave-type">
                {t("leave:form.type")}
                <span className="text-destructive"> *</span>
              </Label>
              <Select value={leaveTypeId} onValueChange={setLeaveTypeId} disabled={busy}>
                <SelectTrigger id="leave-type">
                  <SelectValue placeholder={t("leave:form.type")} />
                </SelectTrigger>
                <SelectContent>
                  {options.map((type) => (
                    <SelectItem key={type.id} value={type.id}>
                      {type.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {typesLimited && <p className="mt-1 text-xs text-muted-foreground">{t("leave:form.typesLimited")}</p>}
              {selected && (
                <div className="mt-2 space-y-1 text-xs text-muted-foreground">
                  {selected.description && <p>{selected.description}</p>}
                  {selected.availableBalance !== null && <p>{t("leave:form.available", { days: days(selected.availableBalance) })}</p>}
                  {selected.perRequestMaxDays !== null && <p>{t("leave:form.perRequestMax", { days: days(selected.perRequestMaxDays) })}</p>}
                </div>
              )}
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="leave-start">
                  {t("leave:form.startDate")}
                  <span className="text-destructive"> *</span>
                </Label>
                <Input id="leave-start" type="date" value={startDate} disabled={busy} onChange={(e) => setStartDate(e.target.value)} />
              </div>
              <div>
                <Label htmlFor="leave-end">
                  {t("leave:form.endDate")}
                  <span className="text-destructive"> *</span>
                </Label>
                <Input id="leave-end" type="date" value={endDate} min={startDate || undefined} disabled={busy} onChange={(e) => setEndDate(e.target.value)} />
              </div>
            </div>
            <div>
              <Label htmlFor="leave-description">
                {t("leave:form.description")}
                {selected?.requiresDescription && <span className="text-destructive"> *</span>}
              </Label>
              <Textarea id="leave-description" value={description} disabled={busy} maxLength={2000} onChange={(e) => setDescription(e.target.value)} />
            </div>
            {editing && <p className="text-xs text-muted-foreground">{t("leave:form.calculatedDays", { days: days(editing.calculatedDays) })}</p>}
            {error && <p className="text-sm text-destructive">{error}</p>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={busy}>
                {busy ? t("common:saving") : editing ? t("common:save") : t("leave:form.submit")}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
