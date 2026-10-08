import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Calculator, Users } from "lucide-react";
import { Button, Group, Stack, Text } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { runPayrollPeriod, type PayrollPeriodStatus } from "@/services/payroll.service";

/**
 * Dönemde bordro satırı yokken gösterilen yönlendirici boş durum: satırların "Hesapla" ile üretildiğini anlatır,
 * dönem açıkken "Hesapla" eylemini (`POST /payroll/periods/{id}/run`) ve Çalışanlar listesine bağlantıyı sunar.
 */
export function PeriodEmptyState({ periodId, status }: { periodId: string; status: PayrollPeriodStatus }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();

  const runMutation = useMutation({
    mutationFn: () => runPayrollPeriod(periodId),
    onSuccess: async (summary) => {
      await queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("payroll:periodDetail.run.success", {
          calculated: summary.calculatedCount,
          skipped: summary.skippedCount,
        }),
      });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("payroll:periodDetail.run.error")),
      }),
  });

  return (
    <Stack align="center" gap="sm" p="lg" data-testid="period-empty-state">
      <Text size="sm" c="dimmed" ta="center" maw={560}>
        {t("payroll:periodDetail.empty")} {t("payroll:periodDetail.emptyState.description")}
      </Text>
      {status === "open" ? (
        <Button
          leftSection={<Calculator size={16} />}
          loading={runMutation.isPending}
          onClick={() => runMutation.mutate()}
        >
          {t("payroll:periodDetail.run.action")}
        </Button>
      ) : (
        <Text size="xs" c="dimmed">
          {t("payroll:periodDetail.emptyState.openOnly")}
        </Text>
      )}
      <Group justify="center">
        <Link to="/app/employees" className="inline-flex items-center gap-1 text-sm hover:underline">
          <Users size={14} /> {t("payroll:periodDetail.emptyState.employeesLink")}
        </Link>
      </Group>
    </Stack>
  );
}
