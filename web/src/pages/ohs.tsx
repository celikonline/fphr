import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { AlertTriangle, HardHat, Plus } from "lucide-react";
import {
  Anchor,
  Badge,
  Button,
  Card,
  Group,
  SimpleGrid,
  Skeleton,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import {
  CloseIncidentDialog,
  IncidentFormDialog,
  SgkNotifiedDialog,
  type LocationOption,
} from "@/components/ohs/incident-dialogs";
import OhsServicesPanel from "@/components/ohs/ohs-services";
import { formatDateOnly } from "@/lib/dates";
import { listLocations } from "@/services/locations.service";
import {
  listIncidents,
  listMyIncidentReports,
  type OhsIncident,
} from "@/services/ohs-incidents.service";

const INCIDENTS_KEY = ["ohs", "incidents"] as const;
const MY_REPORTS_KEY = ["ohs", "incidents", "my"] as const;

/** İSG: İK olay kayıtları (ohs.incidents.view) + çalışanın ramak kala bildirimleri (ohs.my.incident_reports). */
export default function OhsPage() {
  const canIncidents = usePermission("ohs.incidents.view");
  const canReport = usePermission("ohs.my.incident_reports");
  const canServices = usePermission("ohs.services.view");
  if (!canIncidents && !canReport && !canServices) return <NoAccess />;
  return <OhsScreen canIncidents={canIncidents} canReport={canReport} canServices={canServices} />;
}

function useLocationOptions(): {
  options: LocationOption[];
  nameOf: (id?: string | null) => string;
} {
  const canReadLocations = usePermission("organization.locations.read");
  const query = useQuery({
    queryKey: ["organization", "locations", "for-ohs"],
    queryFn: () => listLocations({ page: 1, pageSize: 200, sort: "name" }),
    enabled: canReadLocations,
  });
  const items = query.data?.items ?? [];
  return {
    options: items.map((l) => ({ value: l.id, label: l.name })),
    nameOf: (id) => (id ? (items.find((l) => l.id === id)?.name ?? "—") : "—"),
  };
}

function OhsScreen({
  canIncidents,
  canReport,
  canServices,
}: {
  canIncidents: boolean;
  canReport: boolean;
  canServices: boolean;
}) {
  const { t } = useTranslation(["ohs"]);
  const locations = useLocationOptions();
  const tabs = [
    canIncidents && {
      value: "incidents",
      label: t("ohs:page.incidents"),
      body: <IncidentRegister />,
    },
    canServices && {
      value: "services",
      label: t("ohs:page.services"),
      body: <OhsServicesPanel locations={locations} />,
    },
    canReport && { value: "mine", label: t("ohs:page.myReports"), body: <MyReports /> },
  ].filter((tab) => tab !== false);
  return (
    <Stack gap="md" className="p-6 md:p-10">
      <Title order={2}>{t("ohs:page.title")}</Title>
      {tabs.length === 1 ? (
        tabs[0].body
      ) : (
        <Tabs defaultValue={tabs[0].value} keepMounted={false}>
          <Tabs.List>
            {tabs.map((tab) => (
              <Tabs.Tab key={tab.value} value={tab.value}>
                {tab.label}
              </Tabs.Tab>
            ))}
          </Tabs.List>
          {tabs.map((tab) => (
            <Tabs.Panel key={tab.value} value={tab.value} pt="md">
              {tab.body}
            </Tabs.Panel>
          ))}
        </Tabs>
      )}
    </Stack>
  );
}

type Dialog =
  | { kind: "record" }
  | { kind: "sgk"; incident: OhsIncident }
  | { kind: "close"; incident: OhsIncident }
  | null;

function IncidentRegister() {
  const { t, i18n } = useTranslation(["ohs"]);
  const canManage = usePermission("ohs.incidents.manage");
  const canHazard = usePermission("ohs.hazard_classifications.view");
  const canRisk = usePermission("ohs.risk_assessments.view");
  const queryClient = useQueryClient();
  const locations = useLocationOptions();
  const [dialog, setDialog] = useState<Dialog>(null);
  const query = useQuery({ queryKey: INCIDENTS_KEY, queryFn: listIncidents });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["ohs", "incidents"] });

  const incidents = query.data ?? [];
  const openCount = incidents.filter((i) => !i.closedOn).length;
  const overdueCount = incidents.filter((i) => i.isSgkOverdue).length;
  const pendingSgk = incidents.filter(
    (i) => i.kind === "workAccident" && !i.sgkNotifiedOn && !i.isSgkOverdue
  ).length;
  const date = (value?: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <Stack gap="md">
      <SimpleGrid cols={{ base: 1, sm: 3 }}>
        <SummaryCard label={t("ohs:incidents.summary.open")} value={openCount} />
        <SummaryCard label={t("ohs:incidents.summary.sgkPending")} value={pendingSgk} />
        <SummaryCard
          label={t("ohs:incidents.summary.sgkOverdue")}
          value={overdueCount}
          danger={overdueCount > 0}
        />
      </SimpleGrid>

      {(canHazard || canRisk || canManage) && (
        <Card withBorder padding="md">
          <Group justify="space-between" align="center" wrap="wrap" gap="sm">
            <Group gap="md">
              {canHazard && (
                <Anchor
                  component={Link}
                  to="/app/settings/company/ohs-hazard-classification"
                  size="sm"
                >
                  {t("ohs:page.hazardLink")}
                </Anchor>
              )}
              {canRisk && (
                <Anchor component={Link} to="/app/settings/company/ohs-risk-assessments" size="sm">
                  {t("ohs:page.riskLink")}
                </Anchor>
              )}
            </Group>
            {canManage && (
              <Button
                leftSection={<Plus size={16} />}
                onClick={() => setDialog({ kind: "record" })}
              >
                {t("ohs:incidents.record")}
              </Button>
            )}
          </Group>
        </Card>
      )}

      {query.isLoading && <Skeleton height={160} />}
      {query.isError && <Text c="red">{t("ohs:incidents.loadError")}</Text>}
      {query.isSuccess && incidents.length === 0 && (
        <Card withBorder>
          <Text c="dimmed">{t("ohs:incidents.empty")}</Text>
        </Card>
      )}
      {incidents.length > 0 && (
        <Table.ScrollContainer minWidth={900}>
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("ohs:incidents.fields.eventDate")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.kind")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.employee")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.location")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.description")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.sgk")}</Table.Th>
                <Table.Th>{t("ohs:incidents.fields.status")}</Table.Th>
                {canManage && <Table.Th />}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {incidents.map((incident) => (
                <Table.Tr key={incident.id}>
                  <Table.Td>{date(incident.eventDate)}</Table.Td>
                  <Table.Td>
                    <Badge
                      color={incident.kind === "workAccident" ? "red" : "yellow"}
                      variant="light"
                    >
                      {t(`ohs:incidents.kinds.${incident.kind}`)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <InvolvedCell incident={incident} />
                  </Table.Td>
                  <Table.Td>{locations.nameOf(incident.locationId)}</Table.Td>
                  <Table.Td maw={320}>
                    <Text size="sm" lineClamp={3}>
                      {incident.description}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <SgkCell incident={incident} date={date} />
                  </Table.Td>
                  <Table.Td>
                    <StatusCell incident={incident} />
                  </Table.Td>
                  {canManage && (
                    <Table.Td>
                      <RowActions incident={incident} onOpen={setDialog} />
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      {dialog?.kind === "record" && (
        <IncidentFormDialog
          mode="record"
          locations={locations.options}
          onClose={() => setDialog(null)}
          onSaved={refresh}
        />
      )}
      {dialog?.kind === "sgk" && (
        <SgkNotifiedDialog
          incident={dialog.incident}
          onClose={() => setDialog(null)}
          onDone={refresh}
        />
      )}
      {dialog?.kind === "close" && (
        <CloseIncidentDialog
          incident={dialog.incident}
          onClose={() => setDialog(null)}
          onDone={refresh}
        />
      )}
    </Stack>
  );
}

function InvolvedCell({ incident }: { incident: OhsIncident }) {
  const { t } = useTranslation(["ohs"]);
  if (incident.employeeId) return <EmployeeNameLink id={incident.employeeId} />;
  if (!incident.reportedByEmployeeId) return <>—</>;
  return (
    <Text size="sm">
      {t("ohs:incidents.reportedBy")} <EmployeeNameLink id={incident.reportedByEmployeeId} />
    </Text>
  );
}

function StatusCell({ incident }: { incident: OhsIncident }) {
  const { t } = useTranslation(["ohs"]);
  if (!incident.closedOn) {
    return (
      <Badge color="blue" variant="light">
        {t("ohs:incidents.statuses.open")}
      </Badge>
    );
  }
  return (
    <Stack gap={2}>
      <Badge color="gray" variant="light">
        {t("ohs:incidents.statuses.closed")}
      </Badge>
      {incident.correctiveAction && (
        <Text size="xs" c="dimmed" lineClamp={2}>
          {incident.correctiveAction}
        </Text>
      )}
    </Stack>
  );
}

function RowActions({
  incident,
  onOpen,
}: {
  incident: OhsIncident;
  onOpen: (dialog: Dialog) => void;
}) {
  const { t } = useTranslation(["ohs"]);
  if (incident.closedOn) return null;
  const awaitingSgk = incident.kind === "workAccident" && !incident.sgkNotifiedOn;
  return (
    <Group gap="xs" justify="flex-end" wrap="nowrap">
      {awaitingSgk && (
        <Button size="compact-sm" variant="light" onClick={() => onOpen({ kind: "sgk", incident })}>
          {t("ohs:incidents.markSgk")}
        </Button>
      )}
      <Button
        size="compact-sm"
        variant="subtle"
        disabled={awaitingSgk}
        onClick={() => onOpen({ kind: "close", incident })}
      >
        {t("ohs:incidents.close")}
      </Button>
    </Group>
  );
}

function SgkCell({
  incident,
  date,
}: {
  incident: OhsIncident;
  date: (value?: string | null) => string;
}) {
  const { t } = useTranslation(["ohs"]);
  if (incident.kind !== "workAccident") return <>—</>;
  if (incident.sgkNotifiedOn) {
    return (
      <Badge color="green" variant="light">
        {t("ohs:incidents.sgkNotifiedOn", { date: date(incident.sgkNotifiedOn) })}
      </Badge>
    );
  }
  return (
    <Group gap={4} wrap="nowrap">
      {incident.isSgkOverdue && <AlertTriangle size={14} color="var(--mantine-color-red-6)" />}
      <Text
        size="sm"
        c={incident.isSgkOverdue ? "red" : undefined}
        fw={incident.isSgkOverdue ? 600 : undefined}
      >
        {t(incident.isSgkOverdue ? "ohs:incidents.sgkOverdue" : "ohs:incidents.sgkDue", {
          date: date(incident.sgkNotificationDeadline),
        })}
      </Text>
    </Group>
  );
}

function SummaryCard({
  label,
  value,
  danger = false,
}: {
  label: string;
  value: number;
  danger?: boolean;
}) {
  return (
    <Card withBorder padding="md">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text size="xl" fw={700} c={danger ? "red" : undefined}>
        {value}
      </Text>
    </Card>
  );
}

function MyReports() {
  const { t, i18n } = useTranslation(["ohs"]);
  const queryClient = useQueryClient();
  const locations = useLocationOptions();
  const [reporting, setReporting] = useState(false);
  const query = useQuery({ queryKey: MY_REPORTS_KEY, queryFn: listMyIncidentReports });
  const reports = query.data ?? [];

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group justify="space-between" wrap="wrap">
          <Group gap="sm" wrap="nowrap">
            <HardHat size={20} />
            <Text size="sm">{t("ohs:myReports.intro")}</Text>
          </Group>
          <Button leftSection={<Plus size={16} />} onClick={() => setReporting(true)}>
            {t("ohs:myReports.report")}
          </Button>
        </Group>
      </Card>

      {query.isLoading && <Skeleton height={100} />}
      {query.isError && <Text c="red">{t("ohs:incidents.loadError")}</Text>}
      {query.isSuccess && reports.length === 0 && (
        <Text c="dimmed">{t("ohs:myReports.empty")}</Text>
      )}
      {reports.map((report) => (
        <Card key={report.id} withBorder padding="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Stack gap={4}>
              <Text size="xs" c="dimmed">
                {formatDateOnly(report.eventDate, i18n.language)}
                {report.locationId ? ` · ${locations.nameOf(report.locationId)}` : ""}
              </Text>
              <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
                {report.description}
              </Text>
              {report.correctiveAction && (
                <Text size="sm" c="teal">
                  {t("ohs:myReports.action")}: {report.correctiveAction}
                </Text>
              )}
            </Stack>
            <Badge color={report.closedOn ? "gray" : "blue"} variant="light">
              {t(report.closedOn ? "ohs:incidents.statuses.closed" : "ohs:myReports.underReview")}
            </Badge>
          </Group>
        </Card>
      ))}

      {reporting && (
        <IncidentFormDialog
          mode="report"
          locations={locations.options}
          onClose={() => setReporting(false)}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: MY_REPORTS_KEY })}
        />
      )}
    </Stack>
  );
}
