import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Alert, Badge, Card, Group, SimpleGrid, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { ArrowUpRight, Calculator } from "lucide-react";
import { APP_LAUNCHER_ITEMS, NAV_ITEMS, type NavItem } from "@/config/navigation";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { getApiErrorMessage } from "@/lib/api-error";
import { listPayrollPeriods } from "@/services/payroll.service";

const SECTIONS = [
  { key: "prepare", items: ["employees", "companyLedger", "pdksCenter", "monthlyTimesheet"] },
  { key: "configure", items: ["payItems", "incentives"] },
  { key: "calculate", items: ["payroll", "payslipImport"] },
] as const;
const TOOL_KEYS = new Set<string>(SECTIONS.flatMap((section) => [...section.items]));
const TOOLS = [...NAV_ITEMS, ...APP_LAUNCHER_ITEMS].filter((item) => TOOL_KEYS.has(item.key));

export default function PayrollManagementCenterPage() {
  const { t, i18n } = useTranslation(["payroll", "navigation"]);
  const visible = useVisibleItems(TOOLS);
  const hubVisible = useVisibleItems(APP_LAUNCHER_ITEMS.filter((item) => item.key === "payrollCenter")).length > 0;
  const canReadPeriods = usePermission("payroll.period.recalculate");
  const periods = useQuery({ queryKey: ["payroll", "periods"], queryFn: listPayrollPeriods, enabled: hubVisible && canReadPeriods });
  const byKey = new Map(visible.map((item) => [item.key, item]));
  if (!hubVisible) return <NoAccess />;

  return (
    <Stack gap="xl" className="max-w-7xl p-6 md:p-10">
      <Card withBorder radius="lg" padding="xl">
        <Group justify="space-between">
          <Stack gap="xs">
            <Badge w="fit-content">{t("payroll:center.eyebrow")}</Badge>
            <Title order={1} size="h2">{t("payroll:center.title")}</Title>
            <Text c="dimmed" maw={720}>{t("payroll:center.description")}</Text>
          </Stack>
          <ThemeIcon size={64} radius="xl" variant="light"><Calculator size={30} /></ThemeIcon>
        </Group>
      </Card>
      {SECTIONS.map((section, index) => {
        const items = section.items.map((key) => byKey.get(key)).filter((item): item is NavItem => !!item);
        if (!items.length) return null;
        return (
          <Stack gap="sm" key={section.key}>
            <Title order={2} size="h4">{index + 1}. {t(`payroll:center.sections.${section.key}.title`)}</Title>
            <Text size="sm" c="dimmed">{t(`payroll:center.sections.${section.key}.description`)}</Text>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
              {items.map((item) => {
                const Icon = item.icon;
                return (
                  <Card key={item.key} component={Link} to={item.path} withBorder padding="lg" radius="md">
                    <Group justify="space-between"><Icon size={24} /><ArrowUpRight size={18} /></Group>
                    <Text fw={600} mt="md">{t(`navigation:${item.labelKey}`)}</Text>
                  </Card>
                );
              })}
            </SimpleGrid>
          </Stack>
        );
      })}
      {canReadPeriods && (
        <Stack gap="sm">
          <Title order={2} size="h4">{t("payroll:center.openPeriods")}</Title>
          {periods.isError && <Alert color="red">{getApiErrorMessage(periods.error, t("payroll:genericError"))}</Alert>}
          {periods.isPending && <Text c="dimmed">{t("payroll:center.loading")}</Text>}
          {periods.isSuccess && (
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
              {periods.data.filter((period) => period.status === "open").sort((a, b) => b.year - a.year || b.month - a.month).map((period) => (
                <Card key={period.id} component={Link} to={`/app/payroll/periods/${period.id}`} withBorder radius="md">
                  <Group justify="space-between">
                    <Text fw={600}>{new Intl.DateTimeFormat(i18n.language, { year: "numeric", month: "long" }).format(new Date(period.year, period.month - 1, 1))}</Text>
                    <ArrowUpRight size={18} />
                  </Group>
                  <Text size="sm" c="dimmed" mt="xs">{t("payroll:center.periodHint")}</Text>
                </Card>
              ))}
              {!periods.data.some((period) => period.status === "open") && <Text c="dimmed">{t("payroll:center.noOpenPeriods")}</Text>}
            </SimpleGrid>
          )}
        </Stack>
      )}
      <Alert color="blue" title={t("payroll:center.exitTitle")}>{t("payroll:center.exitHint")}</Alert>
    </Stack>
  );
}
