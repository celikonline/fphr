import { useCallback, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { CalendarRange, ListOrdered } from "lucide-react";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  getEmployeeLeaveLedger,
  type EmployeeLeaveBalances,
  type LeaveBalanceItem,
  type LeaveRequest,
  type LeaveRequestStatus,
  type LeaveRuleSet,
} from "@/services/leave.service";

const ALL_TYPES = "__all__";

/**
 * Seeded rule sets and types store a resource key as their name ("leave.type.annual"); the API returns it as is, so
 * the web localizes it from the `leave:names` map (dots → underscores). Free names from tenants pass through unchanged.
 */
export function useLeaveName() {
  const { t } = useTranslation(["leave"]);
  return useCallback(
    (name: string) => (name.startsWith("leave.") ? t(`leave:names.${name.slice(6).split(".").join("_")}`, { defaultValue: name }) : name),
    [t],
  );
}

export function useDays() {
  const { i18n } = useTranslation();
  return useCallback(
    (value: number | null | undefined) =>
      value === null || value === undefined ? "—" : new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }).format(value),
    [i18n.language],
  );
}

/** Leave type id → display name, from the balances (PerYear types) and, when readable, the employee's rule set (all types). */
export function useLeaveTypeNames(balances: EmployeeLeaveBalances | undefined, ruleSet: LeaveRuleSet | undefined) {
  const leaveName = useLeaveName();
  const { t } = useTranslation(["leave"]);
  const names = useMemo(() => {
    const map = new Map<string, string>();
    ruleSet?.types.forEach((type) => map.set(type.id, leaveName(type.settings.name)));
    balances?.balances.forEach((item) => map.set(item.balance.leaveTypeId, leaveName(item.leaveTypeName)));
    return map;
  }, [balances, ruleSet, leaveName]);
  return useCallback(
    (request: Pick<LeaveRequest, "leaveTypeId" | "leaveTypeCode">) => names.get(request.leaveTypeId) ?? t(`leave:code.${request.leaveTypeCode}`),
    [names, t],
  );
}

const STATUS_VARIANT: Record<LeaveRequestStatus, BadgeProps["variant"]> = {
  pending: "secondary",
  approved: "success",
  rejected: "destructive",
  withdrawn: "outline",
  cancelled: "outline",
};

export function LeaveStatusBadge({ status }: { status: LeaveRequestStatus }) {
  const { t } = useTranslation(["leave"]);
  return <Badge variant={STATUS_VARIANT[status]}>{t(`leave:status.${status}`)}</Badge>;
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  );
}

/** "Özet": the official İzin Özeti fields per PerYear type. */
export function LeaveBalanceSummary({
  data,
  isLoading,
  emptyMessage,
  action,
}: {
  data: EmployeeLeaveBalances | undefined;
  isLoading: boolean;
  emptyMessage?: string;
  action?: ReactNode;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const leaveName = useLeaveName();
  const days = useDays();
  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <div className="flex items-center gap-2">
          <CalendarRange className="h-4 w-4 text-muted-foreground" />
          <CardTitle className="text-lg">{t("leave:summary.title")}</CardTitle>
        </div>
        {action}
      </CardHeader>
      <CardContent className="space-y-6">
        {isLoading ? (
          <Skeleton className="h-32 w-full" />
        ) : !data ? (
          <p className="text-sm text-muted-foreground">{emptyMessage ?? t("leave:summary.unavailable")}</p>
        ) : (
          <>
            <dl className="grid gap-4 sm:grid-cols-3">
              <Field label={t("leave:summary.hireDate")}>{date(data.hireDate)}</Field>
              <Field label={t("leave:summary.accrualStartDate")}>{date(data.leaveAccrualStartDate)}</Field>
            </dl>
            {data.balances.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("leave:summary.noBalances")}</p>
            ) : (
              data.balances.map((item: LeaveBalanceItem) => {
                const b = item.balance;
                return (
                  <div key={b.leaveTypeId} className="space-y-3 border-t pt-4">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <h3 className="font-semibold">{leaveName(item.leaveTypeName)}</h3>
                      <span className="text-sm">
                        {t("leave:summary.available")}: <span className="text-base font-semibold">{days(b.availableBalance)}</span>{" "}
                        {t("leave:days")}
                      </span>
                    </div>
                    <dl className="grid gap-4 sm:grid-cols-3">
                      <Field label={t("leave:summary.currentPeriod")}>
                        {date(b.currentPeriodFrom)} – {date(b.currentPeriodTo)}
                      </Field>
                      <Field label={t("leave:summary.totalAccrued")}>{days(b.totalAccrued)}</Field>
                      <Field label={t("leave:summary.totalUsed")}>{days(b.totalUsed)}</Field>
                      <Field label={t("leave:summary.futureApprovedInPeriod")}>{days(b.futureApprovedInPeriod)}</Field>
                      <Field label={t("leave:summary.currentBalance")}>{days(b.currentBalance)}</Field>
                      <Field label={t("leave:summary.availableBalance")}>{days(b.availableBalance)}</Field>
                      <Field label={t("leave:summary.lastPeriodAccrued")}>{days(b.lastPeriodAccrued)}</Field>
                      <Field label={t("leave:summary.currentPeriodUsed")}>{days(b.currentPeriodUsed)}</Field>
                      <Field label={t("leave:summary.nextPeriodApproved")}>{days(b.nextPeriodApproved)}</Field>
                      <Field label={t("leave:summary.carriedOver")}>{days(b.carriedOverIntoPeriod)}</Field>
                      <Field label={t("leave:summary.extraGrants")}>{days(b.extraGrants)}</Field>
                      <Field label={t("leave:summary.nextAccrualDate")}>{date(b.nextAccrualDate)}</Field>
                    </dl>
                  </div>
                );
              })
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** "Detaylar": the employee's leave ledger ("İzin defteri"), optionally filtered by type. */
export function LeaveLedgerDialog({
  employeeId,
  typeOptions,
  onClose,
}: {
  employeeId: string;
  typeOptions: { id: string; name: string }[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const days = useDays();
  const [typeId, setTypeId] = useState(ALL_TYPES);
  const filter = typeId === ALL_TYPES ? undefined : typeId;
  const { data: rows = [], isLoading } = useQuery({
    queryKey: ["leave", "ledger", employeeId, filter ?? null],
    queryFn: () => getEmployeeLeaveLedger(employeeId, filter),
  });
  const nameOf = (id: string) => typeOptions.find((o) => o.id === id)?.name ?? "—";

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ListOrdered className="h-5 w-5" /> {t("leave:ledger.title")}
          </DialogTitle>
          <DialogDescription>{t("leave:ledger.description")}</DialogDescription>
        </DialogHeader>
        {typeOptions.length > 1 && (
          <div className="max-w-xs">
            <Select value={typeId} onValueChange={setTypeId}>
              <SelectTrigger aria-label={t("leave:ledger.type")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_TYPES}>{t("leave:ledger.allTypes")}</SelectItem>
                {typeOptions.map((option) => (
                  <SelectItem key={option.id} value={option.id}>
                    {option.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
        {isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:ledger.empty")}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("leave:ledger.effectiveDate")}</TableHead>
                <TableHead>{t("leave:ledger.type")}</TableHead>
                <TableHead>{t("leave:ledger.entryType")}</TableHead>
                <TableHead className="text-right">{t("leave:ledger.amount")}</TableHead>
                <TableHead>{t("leave:ledger.source")}</TableHead>
                <TableHead>{t("leave:ledger.reason")}</TableHead>
                <TableHead>{t("leave:ledger.recordedAt")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={row.id}>
                  <TableCell className="whitespace-nowrap">{formatDateOnly(row.effectiveDate, i18n.language)}</TableCell>
                  <TableCell>{nameOf(row.leaveTypeId)}</TableCell>
                  <TableCell>{t(`leave:ledgerType.${row.type}`)}</TableCell>
                  <TableCell className={row.amount < 0 ? "text-right text-destructive" : "text-right"}>
                    {row.amount > 0 ? "+" : ""}
                    {days(row.amount)}
                  </TableCell>
                  <TableCell>{t(`leave:ledgerSource.${row.source}`)}</TableCell>
                  <TableCell className="max-w-[16rem] truncate" title={row.reason ?? undefined}>
                    {row.reason ?? "—"}
                  </TableCell>
                  <TableCell className="whitespace-nowrap text-xs text-muted-foreground">{formatDateTime(row.recordedAt, i18n.language)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        <div className="flex justify-end">
          <Button type="button" variant="outline" onClick={onClose}>
            {t("common:close")}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Requests list (Tür, Başlangıç, Bitiş, Gün, Durum, Kaynak, Açıklama); `actions` renders a trailing cell per row. */
export function LeaveRequestsTable({
  requests,
  typeName,
  actions,
}: {
  requests: LeaveRequest[];
  typeName: (request: LeaveRequest) => string;
  actions?: (request: LeaveRequest) => ReactNode;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const days = useDays();
  const sorted = useMemo(() => [...requests].sort((a, b) => b.startDate.localeCompare(a.startDate)), [requests]);

  if (sorted.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("leave:requests.empty")}</p>;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("leave:requests.type")}</TableHead>
          <TableHead>{t("leave:requests.startDate")}</TableHead>
          <TableHead>{t("leave:requests.endDate")}</TableHead>
          <TableHead className="text-right">{t("leave:requests.days")}</TableHead>
          <TableHead>{t("leave:requests.status")}</TableHead>
          <TableHead>{t("leave:requests.source")}</TableHead>
          <TableHead>{t("leave:requests.description")}</TableHead>
          {actions && <TableHead className="w-0" />}
        </TableRow>
      </TableHeader>
      <TableBody>
        {sorted.map((request) => (
          <TableRow key={request.id}>
            <TableCell className="font-medium">{typeName(request)}</TableCell>
            <TableCell className="whitespace-nowrap">{formatDateOnly(request.startDate, i18n.language)}</TableCell>
            <TableCell className="whitespace-nowrap">{formatDateOnly(request.endDate, i18n.language)}</TableCell>
            <TableCell className="text-right">{days(request.calculatedDays)}</TableCell>
            <TableCell>
              <LeaveStatusBadge status={request.status} />
            </TableCell>
            <TableCell>{t(`leave:requestSource.${request.source}`)}</TableCell>
            <TableCell className="max-w-[16rem] truncate" title={request.description ?? undefined}>
              {request.description ?? "—"}
            </TableCell>
            {actions && <TableCell>{actions(request)}</TableCell>}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
