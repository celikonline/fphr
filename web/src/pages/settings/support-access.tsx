import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ShieldCheck, ShieldOff } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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

      <div className="max-w-xs">
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

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : isError ? (
            <p className="p-6 text-center text-sm text-destructive">{t("settings:supportAccess.loadError")}</p>
          ) : !data || data.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("settings:supportAccess.empty")}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("settings:supportAccess.requestedBy")}</TableHead>
                  <TableHead>{t("settings:supportAccess.reason")}</TableHead>
                  <TableHead>{t("settings:supportAccess.status")}</TableHead>
                  <TableHead>{t("settings:supportAccess.requestedAt")}</TableHead>
                  <TableHead>{t("settings:supportAccess.expiresAt")}</TableHead>
                  <TableHead className="text-right">{t("common:actions")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((grant) => (
                  <TableRow key={grant.id}>
                    <TableCell className="font-medium">
                      {grant.platformUserEmail ?? t("settings:supportAccess.senseStaff")}
                    </TableCell>
                    <TableCell className="max-w-xs whitespace-pre-wrap text-sm">{grant.reason}</TableCell>
                    <TableCell>
                      <Badge variant={STATUS_VARIANT[grant.status] ?? "outline"}>
                        {t(`settings:supportAccess.statuses.${grant.status}`, grant.status)}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm">{formatDateTime(grant.requestedAt, i18n.language)}</TableCell>
                    <TableCell className="text-sm">{formatDateTime(grant.expiresAt, i18n.language)}</TableCell>
                    <TableCell className="space-x-2 whitespace-nowrap text-right">
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
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
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
