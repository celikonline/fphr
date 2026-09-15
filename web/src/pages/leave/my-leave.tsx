import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarDays, Eye, ListChecks, ListOrdered, Pencil, RotateCcw, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { LeaveBalanceSummary, LeaveLedgerDialog, LeaveRequestsTable, useLeaveName, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "@/components/leave/leave-request-dialog";
import { LeaveRequestDetailSheet } from "@/components/leave/leave-request-detail";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  getMyLeaveBalances,
  getMyLeaveRequest,
  getMyLeaveRequests,
  getMyRequestableLeaveTypes,
  previewMyLeaveRequest,
  submitLeaveRequest,
  updatePendingLeaveRequest,
  withdrawLeaveRequest,
  type EmployeeLeaveBalances,
  type LeaveRequest,
  type RequestableLeaveType,
} from "@/services/leave.service";

const MY_LEAVE_KEY = ["leave", "me"] as const;

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
  const [ledgerOpen, setLedgerOpen] = useState(false);
  const [dialog, setDialog] = useState<
    { mode: "add" } | { mode: "edit"; request: LeaveRequest } | { mode: "resubmit"; request: LeaveRequest } | null
  >(null);
  const [withdrawing, setWithdrawing] = useState<LeaveRequest | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);

  const balancesQuery = useQuery({
    queryKey: [...MY_LEAVE_KEY, "balances"],
    queryFn: getMyLeaveBalances,
    enabled: canBalances,
    retry: false,
  });
  const typesQuery = useQuery({ queryKey: [...MY_LEAVE_KEY, "leave-types"], queryFn: getMyRequestableLeaveTypes, enabled: canRequests });
  const requestsQuery = useQuery({ queryKey: [...MY_LEAVE_KEY, "requests"], queryFn: getMyLeaveRequests, enabled: canRequests });

  const typeName = useLeaveTypeNames(balancesQuery.data, undefined);
  const requestableTypes = useMemo(() => buildRequestableTypes(balancesQuery.data, typesQuery.data), [balancesQuery.data, typesQuery.data]);
  const ledgerTypes = useMemo(
    () =>
      typesQuery.data?.map((type) => ({ id: type.id, name: type.name })) ??
      (balancesQuery.data?.balances ?? []).map((item) => ({ id: item.balance.leaveTypeId, name: leaveName(item.leaveTypeName) })),
    [typesQuery.data, balancesQuery.data, leaveName],
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
          action={
            balancesQuery.data ? (
              <Button type="button" size="sm" variant="outline" onClick={() => setLedgerOpen(true)}>
                <ListOrdered className="mr-2 h-4 w-4" /> {t("leave:summary.details")}
              </Button>
            ) : undefined
          }
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
                onOpen={(request) => setDetailId(request.id)}
                actions={(request) => (
                  <div className="flex justify-end gap-1">
                    <Button type="button" variant="ghost" size="icon" title={t("leave:detail.open")} onClick={() => setDetailId(request.id)}>
                      <Eye className="h-4 w-4" />
                    </Button>
                    {request.status === "pending" && (
                      <>
                        <Button type="button" variant="ghost" size="icon" title={t("leave:my.edit")} onClick={() => setDialog({ mode: "edit", request })}>
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button type="button" variant="ghost" size="icon" title={t("leave:my.withdraw")} onClick={() => setWithdrawing(request)}>
                          <Undo2 className="h-4 w-4" />
                        </Button>
                      </>
                    )}
                    {request.status === "rejected" && (
                      <Button type="button" variant="ghost" size="icon" title={t("leave:my.resubmit")} onClick={() => setDialog({ mode: "resubmit", request })}>
                        <RotateCcw className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                )}
              />
            )}
          </CardContent>
        </Card>
      )}

      {detailId && (
        <LeaveRequestDetailSheet
          requestId={detailId}
          scope="me"
          load={getMyLeaveRequest}
          typeName={typeName}
          onClose={() => setDetailId(null)}
          footer={(detail) =>
            detail.status === "rejected" ? (
              <Button
                type="button"
                onClick={() => {
                  setDetailId(null);
                  setDialog({ mode: "resubmit", request: detail });
                }}
              >
                {t("leave:my.resubmit")}
              </Button>
            ) : null
          }
        />
      )}

      {dialog && (
        <LeaveRequestDialog
          key={dialog.mode === "add" ? "add" : `${dialog.mode}-${dialog.request.id}`}
          editing={dialog.mode === "edit" ? dialog.request : null}
          prefill={dialog.mode === "resubmit" ? dialog.request : null}
          types={requestableTypes}
          title={dialog.mode === "edit" ? t("leave:form.editTitle") : dialog.mode === "resubmit" ? t("leave:form.resubmitTitle") : t("leave:form.newTitle")}
          description={dialog.mode === "resubmit" ? t("leave:form.resubmitDescription") : t("leave:form.dialogDescription")}
          submitLabel={dialog.mode === "edit" ? t("common:save") : t("leave:form.submit")}
          typesHint={typesQuery.isError ? t("leave:form.typesLimited") : undefined}
          previewScope="me"
          preview={previewMyLeaveRequest}
          save={(input) =>
            dialog.mode === "edit"
              ? updatePendingLeaveRequest(dialog.request.id, input)
              : submitLeaveRequest(dialog.mode === "resubmit" ? { ...input, previousRequestId: dialog.request.id } : input)
          }
          onClose={() => setDialog(null)}
          onSaved={() => {
            const message = dialog.mode === "edit" ? t("leave:my.updated") : t("leave:my.submitted");
            setDialog(null);
            toast({ variant: "success", title: t("common:success"), description: message });
            void refresh();
          }}
        />
      )}

      {ledgerOpen && <LeaveLedgerDialog own employeeId="me" typeOptions={ledgerTypes} onClose={() => setLedgerOpen(false)} />}

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

/** "+ Talep" types from `/leave/me/leave-types` (with the PerYear balance); falls back to the balance types when that call fails. */
function buildRequestableTypes(balances: EmployeeLeaveBalances | undefined, types: RequestableLeaveType[] | undefined): LeaveRequestTypeOption[] {
  const available = new Map((balances?.balances ?? []).map((item) => [item.balance.leaveTypeId, item.balance.availableBalance]));
  if (types) {
    return types.map((type) => ({
      id: type.id,
      name: type.name,
      description: type.description ?? null,
      requiresDescription: type.requiresDescription,
      perRequestMaxDays: type.perRequestMaxDays ?? null,
      availableBalance: available.get(type.id) ?? null,
    }));
  }
  return (balances?.balances ?? []).map((item) => ({
    id: item.balance.leaveTypeId,
    name: item.leaveTypeName,
    description: null,
    requiresDescription: false,
    perRequestMaxDays: null,
    availableBalance: item.balance.availableBalance,
  }));
}
