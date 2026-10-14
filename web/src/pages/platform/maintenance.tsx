import { useTranslation } from "react-i18next";
import { Activity, CalendarClock, Database, LifeBuoy, ShieldCheck } from "lucide-react";
import { Alert, Badge, Card, Group, Loader, SimpleGrid, Stack, Table, Text, ThemeIcon, Title } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { formatDateTime } from "@/lib/dates";
import {
  getPlatformOperationsOverview,
  listPlatformRecurringJobs,
  PLATFORM_PERMISSIONS,
} from "@/services/platform.service";

const SECTIONS = [
  { key: "backup", icon: Database, color: "blue" },
  { key: "jobs", icon: CalendarClock, color: "teal" },
  { key: "maintenance", icon: Activity, color: "orange" },
  { key: "support", icon: LifeBuoy, color: "cyan" },
  { key: "security", icon: ShieldCheck, color: "violet" },
] as const;

const STATUS_COLORS: Record<string, string> = {
  healthy: "green",
  degraded: "yellow",
  unhealthy: "red",
  succeeded: "green",
  failed: "red",
  processing: "blue",
  enqueued: "yellow",
  scheduled: "gray",
};

export default function PlatformMaintenancePage() {
  const { t, i18n } = useTranslation("navigation");
  const canViewOperations = usePermission(PLATFORM_PERMISSIONS.operationsView);
  const canViewJobs = usePermission(PLATFORM_PERMISSIONS.jobsView);
  const overview = useQuery({
    queryKey: ["platform", "maintenance", "overview"],
    queryFn: getPlatformOperationsOverview,
    enabled: canViewOperations,
    refetchInterval: 60_000,
  });
  const jobs = useQuery({
    queryKey: ["platform", "maintenance", "jobs", 0, 50],
    queryFn: () => listPlatformRecurringJobs(0, 50),
    enabled: canViewJobs,
    refetchInterval: 60_000,
  });

  if (!canViewOperations) return <NoAccess />;

  return (
    <div className="max-w-7xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2">{t("platformMaintenance.title")}</Title>
        <Text c="dimmed" mt="xs">{t("platformMaintenance.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <div>
            <Text fw={600}>{t("platformMaintenance.overviewTitle")}</Text>
            <Text size="sm" c="dimmed">{t("platformMaintenance.overviewDesc")}</Text>
          </div>
          {overview.isFetching && <Loader size="sm" />}
          {overview.data && (
            <Badge color={STATUS_COLORS[overview.data.status] ?? "gray"} variant="light">
              {t(`platformMaintenance.status.${overview.data.status}`)}
            </Badge>
          )}
        </Group>

        {overview.isError && (
          <Alert color="red" title={t("platformMaintenance.loadError")} mb="md">
            {t("platformMaintenance.loadErrorDesc")}
          </Alert>
        )}

        {overview.data && (
          <>
            <SimpleGrid cols={{ base: 2, sm: 3, lg: 5 }} spacing="sm" mb="md">
              {[
                ["servers", overview.data.jobs.serverCount],
                ["enqueued", overview.data.jobs.enqueuedCount],
                ["scheduled", overview.data.jobs.scheduledCount],
                ["processing", overview.data.jobs.processingCount],
                ["failed", overview.data.jobs.failedCount],
              ].map(([key, value]) => (
                <Card key={key} withBorder padding="sm">
                  <Text size="xs" c="dimmed">{t(`platformMaintenance.counters.${key}`)}</Text>
                  <Text size="xl" fw={700}>{value}</Text>
                </Card>
              ))}
            </SimpleGrid>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="sm">
              {overview.data.components.map((component) => (
                <Group key={component.name} gap="xs" wrap="nowrap">
                  <Badge color={STATUS_COLORS[component.status] ?? "gray"} variant="dot">
                    {t(`platformMaintenance.status.${component.status}`)}
                  </Badge>
                  <Text size="sm" lineClamp={1}>{component.name}</Text>
                  <Text size="xs" c="dimmed">{Math.round(component.durationMs)} ms</Text>
                </Group>
              ))}
            </SimpleGrid>
          </>
        )}
      </Card>

      {canViewJobs && (
        <Card withBorder padding="lg">
          <Group justify="space-between" mb="md">
            <div>
              <Text fw={600}>{t("platformMaintenance.jobListTitle")}</Text>
              <Text size="sm" c="dimmed">{t("platformMaintenance.jobListDesc")}</Text>
            </div>
            <Badge variant="light" color="teal">{t("platformMaintenance.available")}</Badge>
          </Group>

          {jobs.isError && <Alert color="red" title={t("platformMaintenance.loadError")}>{t("platformMaintenance.loadErrorDesc")}</Alert>}
          {jobs.isLoading && <Loader size="sm" />}
          {jobs.data && jobs.data.length === 0 && <Text size="sm" c="dimmed">{t("platformMaintenance.noJobs")}</Text>}
          {jobs.data && jobs.data.length > 0 && (
            <Table.ScrollContainer minWidth={760}>
              <Table striped highlightOnHover withTableBorder>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("platformMaintenance.columns.job")}</Table.Th>
                    <Table.Th>{t("platformMaintenance.columns.tenant")}</Table.Th>
                    <Table.Th>{t("platformMaintenance.columns.cron")}</Table.Th>
                    <Table.Th>{t("platformMaintenance.columns.lastRun")}</Table.Th>
                    <Table.Th>{t("platformMaintenance.columns.nextRun")}</Table.Th>
                    <Table.Th>{t("platformMaintenance.columns.state")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {jobs.data.map((job) => (
                    <Table.Tr key={`${job.jobName}:${job.tenantId}`}>
                      <Table.Td>{job.jobName}</Table.Td>
                      <Table.Td><Text size="xs" ff="monospace">{job.tenantId}</Text></Table.Td>
                      <Table.Td><Text size="sm" ff="monospace">{job.cronExpression}</Text></Table.Td>
                      <Table.Td>{formatDateTime(job.lastExecutionUtc, i18n.language)}</Table.Td>
                      <Table.Td>{formatDateTime(job.nextExecutionUtc, i18n.language)}</Table.Td>
                      <Table.Td>
                        <Badge variant="light" color={STATUS_COLORS[job.lastState?.toLowerCase() ?? ""] ?? "gray"}>
                          {job.lastState || "—"}
                        </Badge>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          )}
        </Card>
      )}

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        {SECTIONS.map(({ key, icon: Icon, color }) => (
          <Card key={key} withBorder padding="lg">
            <Stack gap="sm">
              <Group justify="space-between" align="flex-start">
                <ThemeIcon variant="light" color={color} size="lg" radius="md">
                  <Icon size={20} />
                </ThemeIcon>
                <Badge variant="light" color={key === "jobs" && canViewJobs ? "teal" : "gray"}>
                  {key === "jobs" && canViewJobs ? t("platformMaintenance.available") : t("platformMaintenance.planned")}
                </Badge>
              </Group>
              <div>
                <Text fw={600}>{t(`platformMaintenance.${key}Title`)}</Text>
                <Text size="sm" c="dimmed" mt="xs">{t(`platformMaintenance.${key}Desc`)}</Text>
              </div>
            </Stack>
          </Card>
        ))}
      </SimpleGrid>

      <Card withBorder padding="lg" bg="var(--mantine-color-body)">
        <Text fw={600}>{t("platformMaintenance.scopeTitle")}</Text>
        <Text size="sm" c="dimmed" mt="xs">{t("platformMaintenance.scopeDesc")}</Text>
      </Card>
    </div>
  );
}
