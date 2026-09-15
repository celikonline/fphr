import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Clock, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { OvertimeRequestDialog, OvertimesTable, formatTimeOnly } from "@/components/leave/overtime-shared";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { getMyOvertimes, submitMyOvertime, withdrawMyOvertime, type OvertimeRequest } from "@/services/leave.service";

const MY_OVERTIMES_KEY = ["leave", "overtimes", "me"] as const;

/** İzinler → Fazla mesai: the user's own overtime requests (leave.my.overtimes). */
export default function MyOvertimesPage() {
  const canUse = usePermission("leave.my.overtimes");
  if (!canUse) return <NoAccess />;
  return <MyOvertimes />;
}

function MyOvertimes() {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [withdrawing, setWithdrawing] = useState<OvertimeRequest | null>(null);
  const query = useQuery({ queryKey: MY_OVERTIMES_KEY, queryFn: getMyOvertimes });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["leave", "overtimes"] });

  const withdrawMutation = useMutation({
    mutationFn: (row: OvertimeRequest) => withdrawMyOvertime(row.id),
    onSuccess: () => {
      setWithdrawing(null);
      toast({ variant: "success", title: t("common:success"), description: t("leave:overtime.withdrawn") });
      void refresh();
    },
    onError: (error) => {
      setWithdrawing(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) });
    },
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Clock className="h-6 w-6" /> {t("leave:overtime.myTitle")}
          </h1>
          <p className="text-muted-foreground">{t("leave:overtime.mySubtitle")}</p>
        </div>
        <Button type="button" onClick={() => setAdding(true)}>
          {t("leave:overtime.newRequest")}
        </Button>
      </div>

      <Card>
        <CardContent className="overflow-x-auto pt-6">
          {query.isLoading ? (
            <Skeleton className="h-32 w-full" />
          ) : query.isError ? (
            <p className="text-sm text-destructive">{getApiErrorMessage(query.error, t("leave:genericError"))}</p>
          ) : (
            <OvertimesTable
              rows={query.data ?? []}
              actions={(row) =>
                row.status === "pending" ? (
                  <div className="flex justify-end">
                    <Button type="button" variant="ghost" size="icon" title={t("leave:my.withdraw")} onClick={() => setWithdrawing(row)}>
                      <Undo2 className="h-4 w-4" />
                    </Button>
                  </div>
                ) : null
              }
            />
          )}
        </CardContent>
      </Card>

      {adding && (
        <OvertimeRequestDialog
          title={t("leave:overtime.newTitle")}
          description={t("leave:overtime.newDescription")}
          submitLabel={t("leave:form.submit")}
          save={submitMyOvertime}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:overtime.submitted") });
            void refresh();
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(withdrawing)}
        onOpenChange={(open) => !open && setWithdrawing(null)}
        title={t("leave:overtime.withdrawTitle")}
        description={t("leave:overtime.withdrawDescription", {
          date: withdrawing ? formatDateOnly(withdrawing.workDate, i18n.language) : "",
          time: withdrawing ? formatTimeOnly(withdrawing.startTime) : "",
        })}
        confirmLabel={t("leave:my.withdraw")}
        isLoading={withdrawMutation.isPending}
        onConfirm={() => withdrawing && withdrawMutation.mutate(withdrawing)}
      />
    </div>
  );
}
