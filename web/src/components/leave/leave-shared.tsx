import { useCallback, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarRange, ListOrdered } from "lucide-react";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  getEmployeeLeaveLedger,
  getMyLeaveLedger,
  type EmployeeLeaveBalances,
  type LeaveBalanceItem,
  type LeaveLedgerEntry,
  type LeaveRequest,
  type LeaveRequestStatus,
  type LeaveRuleSet,
} from "@/services/leave.service";

const ALL_TYPES = "__all__";
const PAGE_SIZE = 20;
const REQUESTS_COLUMNS_KEY = "senseik.leave.requests.columns";
const LEDGER_COLUMNS_KEY = "senseik.leave.ledger.columns";
/** Column chooser trigger sized like the surrounding `size="sm"` buttons. */
const COLUMN_CHOOSER_CLASS = "h-9 px-3";

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
    (request: Pick<LeaveRequest, "leaveTypeId" | "leaveTypeCode" | "leaveTypeName">) =>
      request.leaveTypeName ?? names.get(request.leaveTypeId) ?? t(`leave:code.${request.leaveTypeCode}`),
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

export function Field({ label, children }: { label: string; children: ReactNode }) {
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
  ruleSet,
}: {
  data: EmployeeLeaveBalances | undefined;
  isLoading: boolean;
  emptyMessage?: string;
  action?: ReactNode;
  /** Rule set field (name plus an optional change action), shown next to the hire dates. */
  ruleSet?: ReactNode;
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
              {ruleSet && <Field label={t("leave:summary.ruleSet")}>{ruleSet}</Field>}
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

/** "Detaylar": the employee's leave ledger ("İzin defteri"), optionally filtered by type; `own` reads `/leave/me/ledger`. */
export function LeaveLedgerDialog({
  employeeId,
  own = false,
  typeOptions,
  onClose,
}: {
  employeeId: string;
  own?: boolean;
  typeOptions: { id: string; name: string }[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const days = useDays();
  const [typeId, setTypeId] = useState(ALL_TYPES);
  const filter = typeId === ALL_TYPES ? undefined : typeId;
  const { data: rows = [], isLoading } = useQuery({
    queryKey: ["leave", "ledger", own ? "me" : employeeId, filter ?? null],
    queryFn: () => (own ? getMyLeaveLedger(filter) : getEmployeeLeaveLedger(employeeId, filter)),
  });
  const nameOf = (id: string) => typeOptions.find((o) => o.id === id)?.name ?? "—";

  const columns: ColumnDef<LeaveLedgerEntry>[] = [
    {
      id: "effectiveDate",
      accessorKey: "effectiveDate",
      header: t("leave:ledger.effectiveDate"),
      meta: { label: t("leave:ledger.effectiveDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.effectiveDate, i18n.language),
    },
    {
      id: "leaveType",
      accessorFn: (row) => nameOf(row.leaveTypeId),
      header: t("leave:ledger.type"),
      meta: { label: t("leave:ledger.type") },
    },
    {
      id: "entryType",
      accessorFn: (row) => t(`leave:ledgerType.${row.type}`),
      header: t("leave:ledger.entryType"),
      meta: { label: t("leave:ledger.entryType") },
    },
    {
      id: "amount",
      accessorKey: "amount",
      header: t("leave:ledger.amount"),
      meta: { label: t("leave:ledger.amount"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => (
        <span className={row.original.amount < 0 ? "text-destructive" : undefined}>
          {row.original.amount > 0 ? "+" : ""}
          {days(row.original.amount)}
        </span>
      ),
    },
    {
      id: "source",
      accessorFn: (row) => t(`leave:ledgerSource.${row.source}`),
      header: t("leave:ledger.source"),
      meta: { label: t("leave:ledger.source") },
    },
    {
      id: "reason",
      accessorFn: (row) => row.reason ?? "",
      enableSorting: false,
      header: t("leave:ledger.reason"),
      meta: { label: t("leave:ledger.reason"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) => <span title={row.original.reason ?? undefined}>{row.original.reason ?? "—"}</span>,
    },
    {
      id: "recordedAt",
      accessorKey: "recordedAt",
      header: t("leave:ledger.recordedAt"),
      meta: { label: t("leave:ledger.recordedAt"), cellClassName: "whitespace-nowrap text-xs text-muted-foreground" },
      cell: ({ row }) => formatDateTime(row.original.recordedAt, i18n.language),
    },
  ];

  const table = useDataTable({
    columns,
    data: rows,
    getRowId: (row) => row.id,
    columnVisibilityKey: LEDGER_COLUMNS_KEY,
    pageSize: PAGE_SIZE,
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ListOrdered className="h-5 w-5" /> {t("leave:ledger.title")}
          </DialogTitle>
          <DialogDescription>{t("leave:ledger.description")}</DialogDescription>
        </DialogHeader>
        {(typeOptions.length > 1 || rows.length > 0) && (
          <div className="flex flex-wrap items-center justify-between gap-2">
            {typeOptions.length > 1 && (
              <div className="w-full max-w-xs">
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
            {rows.length > 0 && <DataTableViewOptions table={table} className={`ml-auto ${COLUMN_CHOOSER_CLASS}`} />}
          </div>
        )}
        {isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:ledger.empty")}</p>
        ) : (
          <DataTable table={table} />
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

/**
 * Requests list (Tür, Başlangıç, Bitiş, Gün, Durum, Kaynak, Açıklama); `actions` renders a trailing cell per row and
 * `onOpen` makes rows open the request detail.
 */
export function LeaveRequestsTable({
  requests,
  typeName,
  actions,
  onOpen,
}: {
  requests: LeaveRequest[];
  typeName: (request: LeaveRequest) => string;
  actions?: (request: LeaveRequest) => ReactNode;
  onOpen?: (request: LeaveRequest) => void;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const days = useDays();
  // Newest first until the user sorts by a column.
  const sorted = useMemo(() => [...requests].sort((a, b) => b.startDate.localeCompare(a.startDate)), [requests]);

  const columns: ColumnDef<LeaveRequest>[] = [
    {
      id: "leaveType",
      accessorFn: (request) => typeName(request),
      enableHiding: false,
      header: t("leave:requests.type"),
      meta: { label: t("leave:requests.type"), cellClassName: "font-medium" },
      cell: ({ row }) =>
        onOpen ? (
          <button
            type="button"
            className="text-left hover:underline"
            onClick={(event) => {
              event.stopPropagation();
              onOpen(row.original);
            }}
          >
            {typeName(row.original)}
          </button>
        ) : (
          typeName(row.original)
        ),
    },
    {
      id: "startDate",
      accessorKey: "startDate",
      header: t("leave:requests.startDate"),
      meta: { label: t("leave:requests.startDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.startDate, i18n.language),
    },
    {
      id: "endDate",
      accessorKey: "endDate",
      header: t("leave:requests.endDate"),
      meta: { label: t("leave:requests.endDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.endDate, i18n.language),
    },
    {
      id: "days",
      accessorKey: "calculatedDays",
      header: t("leave:requests.days"),
      meta: { label: t("leave:requests.days"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => days(row.original.calculatedDays),
    },
    {
      id: "status",
      accessorFn: (request) => t(`leave:status.${request.status}`),
      header: t("leave:requests.status"),
      meta: { label: t("leave:requests.status") },
      cell: ({ row }) => (
        <>
          <LeaveStatusBadge status={row.original.status} />
          {row.original.status === "cancelled" && row.original.cancellationReason && (
            <p className="mt-1 max-w-[12rem] truncate text-xs text-muted-foreground" title={row.original.cancellationReason}>
              {row.original.cancellationReason}
            </p>
          )}
        </>
      ),
    },
    {
      id: "source",
      accessorFn: (request) => t(`leave:requestSource.${request.source}`),
      header: t("leave:requests.source"),
      meta: { label: t("leave:requests.source") },
    },
    {
      id: "description",
      accessorFn: (request) => request.description ?? "",
      enableSorting: false,
      header: t("leave:requests.description"),
      meta: { label: t("leave:requests.description"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) => <span title={row.original.description ?? undefined}>{row.original.description ?? "—"}</span>,
    },
    ...(actions
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => null,
            meta: { headerClassName: "w-0" },
            cell: ({ row }) => <div onClick={(event) => event.stopPropagation()}>{actions(row.original)}</div>,
          } satisfies ColumnDef<LeaveRequest>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: sorted,
    getRowId: (request) => request.id,
    columnVisibilityKey: REQUESTS_COLUMNS_KEY,
    pageSize: PAGE_SIZE,
  });

  if (sorted.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("leave:requests.empty")}</p>;
  }

  return (
    <div className="space-y-2">
      <div className="flex justify-end">
        <DataTableViewOptions table={table} className={COLUMN_CHOOSER_CLASS} />
      </div>
      <DataTable table={table} onRowClick={onOpen} />
    </div>
  );
}
