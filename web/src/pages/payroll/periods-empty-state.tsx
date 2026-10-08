import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { FileUp, Plus, Users } from "lucide-react";
import { Button, Group, Stack, Text } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";

/**
 * Dönem listesi boşken gösterilen yönlendirici boş durum: neden/ne yapılacağı, birincil "Dönem aç" eylemi ve
 * ilgili ekranlara (Çalışanlar, Bordro PDF yükle) bağlantılar.
 */
export function PeriodsEmptyState({ onOpenPeriod }: { onOpenPeriod: () => void }) {
  const { t } = useTranslation("payroll");
  const canImportPayslips = usePermission("payroll.payslip.import");

  return (
    <Stack align="center" gap="sm" p="lg" data-testid="periods-empty-state">
      <Text fw={600}>{t("periods.emptyState.title")}</Text>
      <Text size="sm" c="dimmed" ta="center" maw={560}>
        {t("periods.emptyState.description")}
      </Text>
      <Button leftSection={<Plus size={16} />} onClick={onOpenPeriod}>
        {t("periods.openAction")}
      </Button>
      <Group gap="md" justify="center">
        <Link to="/app/employees" className="inline-flex items-center gap-1 text-sm hover:underline">
          <Users size={14} /> {t("periods.emptyState.employeesLink")}
        </Link>
        {canImportPayslips && (
          <Link to="/app/payroll/payslip-import" className="inline-flex items-center gap-1 text-sm hover:underline">
            <FileUp size={14} /> {t("periods.emptyState.importLink")}
          </Link>
        )}
      </Group>
    </Stack>
  );
}
