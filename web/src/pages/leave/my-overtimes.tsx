import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Button, Card, Group, Skeleton, Text, Title, Tooltip } from "@mantine/core";
import { Clock, Undo2 } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { AnnualOvertimeUsageGauge, OvertimeRequestDialog, OvertimesTable, formatTimeOnly } from "@/components/leave/overtime-shared";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { getMyOvertimeAnnualUsage, getMyOvertimes, submitMyOvertime, withdrawMyOvertime, type OvertimeRequest } from "@/services/leave.service";

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
  // Tek "Talep" hub'ından (/app/requests) `?new=1` ile gelince ekleme diyaloğu ilk render'da açık başlar (roadmap #42).
  const [searchParams, setSearchParams] = useSearchParams();
  const [adding, setAdding] = useState(() => searchParams.get("new") === "1");
  const [withdrawing, setWithdrawing] = useState<OvertimeRequest | null>(null);
  const query = useQuery({ queryKey: MY_OVERTIMES_KEY, queryFn: getMyOvertimes });
  const annualUsageQuery = useQuery({ queryKey: [...MY_OVERTIMES_KEY, "annual-usage"], queryFn: () => getMyOvertimeAnnualUsage(), retry: false });

  useEffect(() => {
    if (searchParams.get("new") === "1") {
      setSearchParams(
        (params) => {
          params.delete("new");
          return params;
        },
        { replace: true },
      );
    }
  }, [searchParams, setSearchParams]);

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
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Clock className="h-6 w-6" /> {t("leave:overtime.myTitle")}
          </Title>
          <Text c="dimmed">{t("leave:overtime.mySubtitle")}</Text>
        </div>
        <Button type="button" onClick={() => setAdding(true)}>
          {t("leave:overtime.newRequest")}
        </Button>
      </Group>

      {annualUsageQuery.data && (
        <Card withBorder padding="sm">
          <AnnualOvertimeUsageGauge usage={annualUsageQuery.data} />
        </Card>
      )}

      <Card withBorder padding="lg">
        <div className="overflow-x-auto">
          {query.isLoading ? (
            <Skeleton h={128} />
          ) : query.isError ? (
            <Text size="sm" c="red">
              {getApiErrorMessage(query.error, t("leave:genericError"))}
            </Text>
          ) : (
            <OvertimesTable
              rows={query.data ?? []}
              actions={(row) =>
                row.status === "pending" ? (
                  <Group justify="flex-end" wrap="nowrap">
                    <Tooltip label={t("leave:my.withdraw")}>
                      <ActionIcon
                        type="button"
                        variant="subtle"
                        color="gray"
                        size="lg"
                        aria-label={t("leave:my.withdraw")}
                        onClick={() => setWithdrawing(row)}
                      >
                        <Undo2 className="h-4 w-4" />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                ) : null
              }
            />
          )}
        </div>
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
