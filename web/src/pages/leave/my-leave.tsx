import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Button, Card, Group, Skeleton, Text, Title, Tooltip } from "@mantine/core";
import { CalendarDays, Eye, ListChecks, ListOrdered, Pencil, RotateCcw, Undo2, type LucideIcon } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { LeaveBalanceSummary, LeaveLedgerDialog, LeaveRequestsTable, useLeaveName, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "@/components/leave/leave-request-dialog";
import { LeaveRequestDetailSheet } from "@/components/leave/leave-request-detail";
import { DIALOG_SEARCH_PARAM, useDialogHistorySync } from "@/hooks";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { useAuthStore } from "@/store/auth.store";
import {
  getMyLeaveBalances,
  getMyLeaveRequest,
  getMyLeaveRequests,
  getMyRequestableLeaveTypes,
  previewMyLeaveRequest,
  requestableBalance,
  submitLeaveRequest,
  updatePendingLeaveRequest,
  withdrawLeaveRequest,
  type EmployeeLeaveBalances,
  type LeaveRequest,
  type RequestableLeaveType,
} from "@/services/leave.service";

const MY_LEAVE_KEY = ["leave", "me"] as const;

type LeaveDialogState = { mode: "add" } | { mode: "edit"; request: LeaveRequest } | { mode: "resubmit"; request: LeaveRequest };

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
  const employeeId = useAuthStore((state) => state.user?.employeeId);
  const [ledgerOpen, setLedgerOpen] = useState(false);
  // Tek "Talep" hub'ından (/app/requests) `?new=1` ile gelince ekleme diyaloğu ilk render'da açık başlar (roadmap #42).
  const [searchParams, setSearchParams] = useSearchParams();
  // senseik-022: dialog açık/kapalı durumu URL'e (`?dialog=leave-request`) senkronize — tarayıcı geri
  // tuşu diyaloğu kapatır, sayfadan çıkarmaz. `dialog` state'i hangi mod (add/edit/resubmit) ve hangi
  // kayıt olduğunu hâlâ yerelde tutuyor (URL bunu taşımıyor); `dialogUrlOpen` sadece "açık mı" sinyali.
  const { isOpen: dialogUrlOpen, open: openDialogUrl, close: closeDialogUrl } = useDialogHistorySync("leave-request");
  const [dialog, setDialog] = useState<LeaveDialogState | null>(() =>
    canRequests && searchParams.get("new") === "1" ? { mode: "add" } : null,
  );
  const [withdrawing, setWithdrawing] = useState<LeaveRequest | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);

  useEffect(() => {
    if (searchParams.get("new") === "1") {
      // `new=1` zaten kendi history girdisiyle geldi (hub'daki Link tıklaması) — burada `replace` ile
      // aynı girdiyi `?dialog=leave-request` işaretine çeviriyoruz, ayrı bir history girdisi eklemiyoruz.
      setSearchParams(
        (params) => {
          const next = new URLSearchParams(params);
          next.delete("new");
          next.set(DIALOG_SEARCH_PARAM, "leave-request");
          return next;
        },
        { replace: true },
      );
    }
  }, [searchParams, setSearchParams]);

  // Tarayıcı geri tuşu `?dialog=leave-request` işaretini kaldırınca diyaloğu kapat (senseik-022).
  // Render sırasında düzeltiliyor (efekt içinde değil — employee-list.tsx'teki `prevFilter` deseniyle
  // aynı "türetilmiş state'i render'da senkronize et" yaklaşımı; `react-hooks/set-state-in-effect` uyarısını önler).
  const [dialogWasUrlOpen, setDialogWasUrlOpen] = useState(dialogUrlOpen);
  if (dialogWasUrlOpen !== dialogUrlOpen) {
    setDialogWasUrlOpen(dialogUrlOpen);
    if (!dialogUrlOpen) setDialog(null);
  }

  function openLeaveDialog(next: LeaveDialogState) {
    setDialog(next);
    openDialogUrl();
  }

  function closeLeaveDialog() {
    setDialog(null);
    closeDialogUrl();
  }

  const balancesQuery = useQuery({
    queryKey: [...MY_LEAVE_KEY, "balances"],
    queryFn: getMyLeaveBalances,
    enabled: canBalances,
    retry: false,
  });
  const typesQuery = useQuery({ queryKey: [...MY_LEAVE_KEY, "leave-types"], queryFn: getMyRequestableLeaveTypes, enabled: canRequests });
  const requestsQuery = useQuery({ queryKey: [...MY_LEAVE_KEY, "requests"], queryFn: getMyLeaveRequests, enabled: canRequests });

  const typeName = useLeaveTypeNames(balancesQuery.data, undefined);
  const requestableTypes = useMemo(
    () => buildRequestableTypes(balancesQuery.data, typesQuery.data, leaveName),
    [balancesQuery.data, typesQuery.data, leaveName],
  );
  // senseik-039: `typesQuery.data[].name` artık API'den ham anahtar olarak geliyor (bkz. buildRequestableTypes),
  // ledger filtresi de her yerdeki gibi `leaveName()`'den geçmeli.
  const ledgerTypes = useMemo(
    () =>
      typesQuery.data?.map((type) => ({ id: type.id, name: leaveName(type.name) })) ??
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
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarDays className="h-6 w-6" /> {t("leave:my.title")}
          </Title>
          <Text c="dimmed">{t("leave:my.subtitle")}</Text>
        </div>
        {canRequests && (
          <Button type="button" onClick={() => openLeaveDialog({ mode: "add" })}>
            {t("leave:my.newRequest")}
          </Button>
        )}
      </Group>

      {canBalances && (
        <LeaveBalanceSummary
          data={balancesQuery.data}
          isLoading={balancesQuery.isLoading}
          emptyMessage={balancesQuery.isError ? getApiErrorMessage(balancesQuery.error, t("leave:summary.unavailable")) : undefined}
          action={
            balancesQuery.data ? (
              <Button type="button" size="sm" variant="default" leftSection={<ListOrdered className="h-4 w-4" />} onClick={() => setLedgerOpen(true)}>
                {t("leave:summary.details")}
              </Button>
            ) : undefined
          }
        />
      )}

      {canRequests && (
        <Card withBorder padding="lg">
          <Group gap="xs" mb="md" wrap="nowrap">
            <ListChecks className="h-4 w-4" color="var(--mantine-color-dimmed)" />
            <Title order={2} size="h4">
              {t("leave:requests.title")}
            </Title>
          </Group>
          {requestsQuery.isLoading ? (
            <Skeleton h={128} />
          ) : requestsQuery.isError ? (
            <Text size="sm" c="red">
              {getApiErrorMessage(requestsQuery.error, t("leave:genericError"))}
            </Text>
          ) : (
            <LeaveRequestsTable
              requests={requestsQuery.data ?? []}
              typeName={typeName}
              onOpen={(request) => setDetailId(request.id)}
              actions={(request) => (
                <Group justify="flex-end" gap={4} wrap="nowrap">
                  <RowAction icon={Eye} label={t("leave:detail.open")} onClick={() => setDetailId(request.id)} />
                  {request.status === "pending" && (
                    <>
                      <RowAction icon={Pencil} label={t("leave:my.edit")} onClick={() => openLeaveDialog({ mode: "edit", request })} />
                      <RowAction icon={Undo2} label={t("leave:my.withdraw")} onClick={() => setWithdrawing(request)} />
                    </>
                  )}
                  {request.status === "rejected" && (
                    <RowAction icon={RotateCcw} label={t("leave:my.resubmit")} onClick={() => openLeaveDialog({ mode: "resubmit", request })} />
                  )}
                </Group>
              )}
            />
          )}
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
                  openLeaveDialog({ mode: "resubmit", request: detail });
                }}
              >
                {t("leave:my.resubmit")}
              </Button>
            ) : null
          }
        />
      )}

      {dialog && dialogUrlOpen && (
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
          loadDetail={getMyLeaveRequest}
          requesterEmployeeId={employeeId ?? undefined}
          preview={previewMyLeaveRequest}
          documentUploadScope="me"
          save={(input) =>
            dialog.mode === "edit"
              ? updatePendingLeaveRequest(dialog.request.id, input)
              : submitLeaveRequest(dialog.mode === "resubmit" ? { ...input, previousRequestId: dialog.request.id } : input)
          }
          onClose={closeLeaveDialog}
          onSaved={() => {
            const message = dialog.mode === "edit" ? t("leave:my.updated") : t("leave:my.submitted");
            closeLeaveDialog();
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

/** Icon-only row action with a tooltip; the label doubles as the accessible name. */
function RowAction({ icon: Icon, label, onClick }: { icon: LucideIcon; label: string; onClick: () => void }) {
  return (
    <Tooltip label={label}>
      <ActionIcon type="button" variant="subtle" color="gray" size="lg" aria-label={label} onClick={onClick}>
        <Icon className="h-4 w-4" />
      </ActionIcon>
    </Tooltip>
  );
}

/**
 * "+ Talep" types from `/leave/me/leave-types` (with the PerYear balance); falls back to the balance types when
 * that call fails. senseik-039: `type.name`/`item.leaveTypeName` ham anahtar olarak gelir (ör. "leave.type.annual")
 * — API artık bunları çevirmiyor, bu yüzden burada da `leave-section.tsx`'teki `addLeaveTypes`
 * (İzin Ekle — HR'ın çalışan adına oluşturduğu form) ile AYNI şekilde `leaveName()`'den geçiriyoruz. Öncesinde bu
 * fonksiyon `leaveName()` çağırmıyordu ve API'nin (kullanıcının uygulama içi dil tercihinden bağımsız,
 * Accept-Language başlığına göre) çevirdiği ham metne güveniyordu — "+ Talep" açılır listesinin tek başına
 * İngilizce görünmesinin kök nedeni buydu.
 */
export function buildRequestableTypes(
  balances: EmployeeLeaveBalances | undefined,
  types: RequestableLeaveType[] | undefined,
  leaveName: (name: string) => string,
): LeaveRequestTypeOption[] {
  const available = new Map((balances?.balances ?? []).map((item) => [item.balance.leaveTypeId, requestableBalance(item)]));
  if (types) {
    return types.map((type) => ({
      id: type.id,
      name: leaveName(type.name),
      description: type.description ?? null,
      requiresDescription: type.requiresDescription,
      perRequestMaxDays: type.perRequestMaxDays ?? null,
      availableBalance: available.get(type.id) ?? null,
      unit: type.unit,
      requiresSubstitute: type.requiresSubstitute ?? false,
      requiresDocument: type.requiresDocument,
    }));
  }
  // Balance-only fallback (leave-types call failed): unit unknown, so the form stays FullDay-only.
  return (balances?.balances ?? []).map((item) => ({
    id: item.balance.leaveTypeId,
    name: leaveName(item.leaveTypeName),
    description: null,
    requiresDescription: false,
    perRequestMaxDays: null,
    availableBalance: requestableBalance(item),
  }));
}
