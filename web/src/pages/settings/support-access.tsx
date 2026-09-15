import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Check, ShieldCheck, ShieldOff } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  IMPERSONATION_GRANT_STATUSES,
  approveImpersonationGrant,
  listImpersonationGrants,
  revokeImpersonationGrant,
  type ImpersonationGrant,
  type ImpersonationGrantStatus,
} from "@/services/impersonation.service";

const ALL = "all";

const STATUS_VARIANT: Record<ImpersonationGrantStatus, BadgeProps["variant"]> = {
  Pending: "secondary",
  Approved: "default",
  Active: "success",
  Expired: "outline",
  Revoked: "destructive",
};

const REVOCABLE: ImpersonationGrantStatus[] = ["Pending", "Approved", "Active"];

/** Lifecycle order used when sorting by status. */
const STATUS_ORDER = IMPERSONATION_GRANT_STATUSES;
const PAGE_SIZE = 20;
/** Stable empty list so client paging does not see new data (and reset) on every render while loading. */
const NO_GRANTS: ImpersonationGrant[] = [];
const COLUMN_VISIBILITY_KEY = "senseik.settings.supportAccess.columns";

/** Chronological compare of ISO instants; missing values sort last. */
function compareInstants(a: string | null, b: string | null): number {
  if (a === b) return 0;
  if (!a) return 1;
  if (!b) return -1;
  return new Date(a).getTime() - new Date(b).getTime();
}

type PendingAction = { kind: "approve" | "revoke"; grant: ImpersonationGrant } | null;

export default function SupportAccessPage() {
  const canApprove = usePermission("identity.impersonation.approve");
  if (!canApprove) return <NoAccess />;
  return <SupportAccessList />;
}

function SupportAccessList() {
  const { t, i18n } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<string>(ALL);
  const [pending, setPending] = useState<PendingAction>(null);

  const { data, isLoading, isError } = useQuery({
    queryKey: ["identity", "impersonation-grants", status],
    queryFn: () => listImpersonationGrants(status === ALL ? undefined : status),
  });

  const onDone = (successKey: string) => {
    queryClient.invalidateQueries({ queryKey: ["identity", "impersonation-grants"] });
    toast({ variant: "success", title: t("common:success"), description: t(successKey) });
    setPending(null);
  };
  const onFail = (error: unknown) => {
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("settings:supportAccess.actionError")),
    });
    setPending(null);
  };

  const approveMutation = useMutation({
    mutationFn: approveImpersonationGrant,
    onSuccess: () => onDone("settings:supportAccess.approveSuccess"),
    onError: onFail,
  });
  const revokeMutation = useMutation({
    mutationFn: revokeImpersonationGrant,
    onSuccess: () => onDone("settings:supportAccess.revokeSuccess"),
    onError: onFail,
  });

  function confirm() {
    if (!pending) return;
    if (pending.kind === "approve") approveMutation.mutate(pending.grant.id);
    else revokeMutation.mutate(pending.grant.id);
  }

  const locale = i18n.language;
  const requesterOf = (grant: ImpersonationGrant) => grant.platformUserEmail ?? t("settings:supportAccess.senseStaff");

  const columns: ColumnDef<ImpersonationGrant>[] = [
    {
      id: "requestedBy",
      accessorFn: requesterOf,
      enableHiding: false,
      header: t("settings:supportAccess.requestedBy"),
      meta: { label: t("settings:supportAccess.requestedBy"), cellClassName: "font-medium" },
      sortingFn: (a, b) => requesterOf(a.original).localeCompare(requesterOf(b.original), locale),
    },
    {
      id: "reason",
      accessorKey: "reason",
      enableSorting: false,
      header: t("settings:supportAccess.reason"),
      meta: { label: t("settings:supportAccess.reason"), cellClassName: "max-w-xs whitespace-pre-wrap text-sm" },
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("settings:supportAccess.status"),
      meta: { label: t("settings:supportAccess.status") },
      sortingFn: (a, b) => STATUS_ORDER.indexOf(a.original.status) - STATUS_ORDER.indexOf(b.original.status),
      cell: ({ row }) => (
        <Badge variant={STATUS_VARIANT[row.original.status] ?? "outline"}>
          {t(`settings:supportAccess.statuses.${row.original.status}`, row.original.status)}
        </Badge>
      ),
    },
    {
      id: "requestedAt",
      accessorKey: "requestedAt",
      header: t("settings:supportAccess.requestedAt"),
      meta: { label: t("settings:supportAccess.requestedAt"), cellClassName: "whitespace-nowrap text-sm" },
      sortingFn: (a, b) => compareInstants(a.original.requestedAt, b.original.requestedAt),
      cell: ({ row }) => formatDateTime(row.original.requestedAt, locale),
    },
    {
      id: "expiresAt",
      accessorKey: "expiresAt",
      header: t("settings:supportAccess.expiresAt"),
      meta: { label: t("settings:supportAccess.expiresAt"), cellClassName: "whitespace-nowrap text-sm" },
      sortingFn: (a, b) => compareInstants(a.original.expiresAt, b.original.expiresAt),
      cell: ({ row }) => formatDateTime(row.original.expiresAt, locale),
    },
    {
      id: "actions",
      enableHiding: false,
      enableSorting: false,
      header: t("common:actions"),
      meta: { headerClassName: "text-right", cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => {
        const grant = row.original;
        return (
          <div className="inline-flex gap-2" onClick={(e) => e.stopPropagation()}>
            {grant.status === "Pending" && (
              <Button type="button" size="sm" onClick={() => setPending({ kind: "approve", grant })}>
                <Check className="mr-1 h-4 w-4" /> {t("settings:supportAccess.approve")}
              </Button>
            )}
            {REVOCABLE.includes(grant.status) && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="text-destructive hover:text-destructive"
                onClick={() => setPending({ kind: "revoke", grant })}
              >
                <ShieldOff className="mr-1 h-4 w-4" /> {t("settings:supportAccess.revoke")}
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  const table = useDataTable({
    columns,
    data: data ?? NO_GRANTS,
    getRowId: (grant) => grant.id,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
    pageSize: PAGE_SIZE,
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <ShieldCheck className="h-6 w-6" /> {t("settings:supportAccess.title")}
        </h1>
        <p className="text-muted-foreground">{t("settings:supportAccess.subtitle")}</p>
      </div>

      <Alert>
        <ShieldCheck className="h-4 w-4" />
        <AlertTitle>{t("settings:supportAccess.privacyTitle")}</AlertTitle>
        <AlertDescription>{t("settings:supportAccess.privacyText")}</AlertDescription>
      </Alert>

      <div className="flex flex-wrap items-center gap-3">
        <div className="w-full max-w-xs">
          <Select value={status} onValueChange={setStatus}>
            <SelectTrigger aria-label={t("settings:supportAccess.status")}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>{t("settings:supportAccess.allStatuses")}</SelectItem>
              {IMPERSONATION_GRANT_STATUSES.map((value) => (
                <SelectItem key={value} value={value}>
                  {t(`settings:supportAccess.statuses.${value}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <DataTableViewOptions table={table} className="ml-auto" />
      </div>

      <Card>
        <CardContent className="p-0">
          {isError ? (
            <p className="p-6 text-center text-sm text-destructive">{t("settings:supportAccess.loadError")}</p>
          ) : (
            <DataTable table={table} isLoading={isLoading} emptyMessage={t("settings:supportAccess.empty")} />
          )}
        </CardContent>
      </Card>

      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => !open && setPending(null)}
        title={
          pending?.kind === "approve" ? t("settings:supportAccess.approveTitle") : t("settings:supportAccess.revokeTitle")
        }
        description={
          pending?.kind === "approve"
            ? t("settings:supportAccess.approveConfirm", { requester: pending.grant.platformUserEmail ?? t("settings:supportAccess.senseStaff") })
            : t("settings:supportAccess.revokeConfirm")
        }
        confirmLabel={pending?.kind === "approve" ? t("settings:supportAccess.approve") : t("settings:supportAccess.revoke")}
        confirmVariant={pending?.kind === "approve" ? "default" : "destructive"}
        onConfirm={confirm}
        isLoading={approveMutation.isPending || revokeMutation.isPending}
      />
    </div>
  );
}
