import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Badge, Card, Group, SimpleGrid, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { ArrowUpRight, QrCode } from "lucide-react";
import { APP_LAUNCHER_ITEMS, navIconColor, type NavItem } from "@/config/navigation";
import NoAccess from "@/components/no-access";
import { useVisibleItems } from "@/hooks/use-nav-visibility";

const INTEGRATION_ITEM: NavItem = {
  key: "pdksIntegrations",
  labelKey: "settings:pdksIntegrations",
  path: "/app/settings/pdks-integrations",
  icon: QrCode,
  permissions: ["attendance.terminal.view", "attendance.terminal.manage"],
  iconColor: "orange",
};

const ATTENDANCE_ITEMS = [
  ...APP_LAUNCHER_ITEMS.filter(
    (item) => item.path.startsWith("/app/attendance/") && item.key !== "pdksCenter",
  ),
  INTEGRATION_ITEM,
];

const SECTIONS = [
  { key: "operations", items: ["dailyList", "timeDashboard", "liveBoard"] },
  { key: "timesheets", items: ["timesheet", "teamTimesheet", "monthlyTimesheet", "monthlyMatrix", "punchRequests"] },
  { key: "planning", items: ["shifts", "shiftPlan", "shiftDefinitions", "rotations"] },
  { key: "devices", items: ["selfCheckIn", "deviceLogImport", "terminals", "pdksIntegrations"] },
] as const;

export default function PDKSManagementCenterPage() {
  const { t } = useTranslation(["navigation", "settings"]);
  const visibleItems = useVisibleItems(ATTENDANCE_ITEMS);
  const hubVisible = useVisibleItems(APP_LAUNCHER_ITEMS.filter((item) => item.key === "pdksCenter")).length > 0;
  const visibleByKey = new Map(visibleItems.map((item) => [item.key, item]));

  if (!hubVisible) return <NoAccess />;

  return (
    <Stack gap="xl" className="max-w-7xl p-6 md:p-10">
      <Card
        radius="lg"
        padding="xl"
        className="overflow-hidden border-0 bg-gradient-to-br from-slate-900 via-slate-800 to-orange-950 text-white"
      >
        <Group justify="space-between" align="center" wrap="nowrap" gap="xl">
          <Stack gap="xs">
            <Badge variant="light" color="orange" w="fit-content">
              {t("pdksCenter.eyebrow")}
            </Badge>
            <Title order={1} size="h2" c="white">
              {t("pdksCenter.title")}
            </Title>
            <Text c="gray.3" maw={680}>
              {t("pdksCenter.subtitle")}
            </Text>
          </Stack>
          <ThemeIcon visibleFrom="sm" size={88} radius="xl" variant="light" color="orange">
            <QrCode size={42} />
          </ThemeIcon>
        </Group>
        <Group mt="xl" gap="xs">
          <Badge size="lg" variant="light" color="gray">
            {t("pdksCenter.availableTools", { count: visibleItems.length })}
          </Badge>
          <Text size="sm" c="gray.4">{t("pdksCenter.accessHint")}</Text>
        </Group>
      </Card>

      {SECTIONS.map((section) => {
        const items = section.items.map((key) => visibleByKey.get(key)).filter((item): item is NavItem => !!item);
        if (items.length === 0) return null;

        return (
          <Stack key={section.key} gap="sm">
            <Title order={2} size="h4">{t(`pdksCenter.sections.${section.key}`)}</Title>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
              {items.map((item) => {
                const Icon = item.icon;
                return (
                  <Card
                    key={item.key}
                    component={Link}
                    to={item.path}
                    withBorder
                    radius="md"
                    padding="lg"
                    className="transition-colors hover:border-orange-400 hover:bg-orange-50/40 dark:hover:bg-dark-6"
                  >
                    <Group wrap="nowrap" align="center">
                      <ThemeIcon variant="light" color="orange" size={46} radius="md">
                        <Icon size={23} color={navIconColor(item)} />
                      </ThemeIcon>
                      <Stack gap={2} className="min-w-0 flex-1">
                        <Text fw={600} size="sm">{t(item.labelKey)}</Text>
                        <Text size="xs" c="dimmed">{t(`pdksCenter.descriptions.${item.key}`)}</Text>
                      </Stack>
                      <ArrowUpRight size={17} className="shrink-0 text-gray-400" />
                    </Group>
                  </Card>
                );
              })}
            </SimpleGrid>
          </Stack>
        );
      })}
    </Stack>
  );
}
