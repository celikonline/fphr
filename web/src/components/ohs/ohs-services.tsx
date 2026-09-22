import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Plus } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Group,
  List,
  SimpleGrid,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
} from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import type { LocationOption } from "@/components/ohs/incident-dialogs";
import {
  AssignmentDialog,
  CommitteeMemberDialog,
  EndPeriodDialog,
} from "@/components/ohs/ohs-service-dialogs";
import { formatDateOnly } from "@/lib/dates";
import {
  endCommitteeMember,
  endSafetyAssignment,
  listCommitteeMembers,
  listLocationServiceSummaries,
  listSafetyAssignments,
  type LocationServiceSummary,
} from "@/services/ohs-services.service";

const SERVICES_KEY = ["ohs", "services"] as const;

const HAZARD_COLORS = { low: "green", medium: "orange", high: "red" } as const;

type Dialog =
  | { kind: "assignment" }
  | { kind: "member" }
  | { kind: "end"; title: string; minDate: string; onEnd: (endDate: string) => Promise<void> }
  | null;

export interface OhsLocations {
  options: LocationOption[];
  nameOf: (id?: string | null) => string;
}

/** İSG hizmetleri: lokasyon uyum kartları + uzman/hekim görevlendirmeleri + İSG kurulu (ohs.services.view). */
export default function OhsServicesPanel({ locations }: { locations: OhsLocations }) {
  const { t, i18n } = useTranslation(["ohs"]);
  const canManage = usePermission("ohs.services.manage");
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<Dialog>(null);
  const summaries = useQuery({
    queryKey: [...SERVICES_KEY, "locations"],
    queryFn: listLocationServiceSummaries,
  });
  const assignments = useQuery({
    queryKey: [...SERVICES_KEY, "assignments"],
    queryFn: listSafetyAssignments,
  });
  const committee = useQuery({
    queryKey: [...SERVICES_KEY, "committee"],
    queryFn: listCommitteeMembers,
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: SERVICES_KEY });
  const date = (value: string) => formatDateOnly(value, i18n.language);
  const period = (start: string, end?: string | null) =>
    end ? `${date(start)} – ${date(end)}` : t("ohs:services.since", { date: date(start) });
  const assignmentRows = assignments.data ?? [];
  const memberRows = committee.data ?? [];

  return (
    <Stack gap="lg">
      <Stack gap="sm">
        <Title order={4}>{t("ohs:services.complianceTitle")}</Title>
        {summaries.isLoading && <Skeleton height={140} />}
        {summaries.isError && <Text c="red">{t("ohs:services.loadError")}</Text>}
        {summaries.isSuccess && summaries.data.length === 0 && (
          <Text c="dimmed">{t("ohs:services.noClassifiedLocations")}</Text>
        )}
        <SimpleGrid cols={{ base: 1, md: 2 }}>
          {(summaries.data ?? []).map((summary) => (
            <LocationCard
              key={summary.locationId}
              summary={summary}
              name={locations.nameOf(summary.locationId)}
            />
          ))}
        </SimpleGrid>
      </Stack>

      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={4}>{t("ohs:services.assignmentsTitle")}</Title>
          {canManage && (
            <Button
              size="sm"
              leftSection={<Plus size={16} />}
              onClick={() => setDialog({ kind: "assignment" })}
            >
              {t("ohs:services.addAssignment")}
            </Button>
          )}
        </Group>
        {assignments.isSuccess && assignmentRows.length === 0 && (
          <Text c="dimmed">{t("ohs:services.noAssignments")}</Text>
        )}
        {assignmentRows.length > 0 && (
          <Table.ScrollContainer minWidth={760}>
            <Table striped verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("ohs:services.fields.role")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.person")}</Table.Th>
                  <Table.Th>{t("ohs:incidents.fields.location")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.monthlyMinutes")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.period")}</Table.Th>
                  {canManage && <Table.Th />}
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {assignmentRows.map((a) => (
                  <Table.Tr key={a.id} opacity={a.endDate ? 0.6 : 1}>
                    <Table.Td>
                      {t(`ohs:services.roles.${a.role}`)}
                      {a.certificateClass && (
                        <Badge ml={6} size="xs" variant="outline">
                          {t(`ohs:services.certificates.${a.certificateClass}`)}
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{a.personName}</Text>
                      {a.providerName && (
                        <Text size="xs" c="dimmed">
                          {a.providerName}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>{locations.nameOf(a.locationId)}</Table.Td>
                    <Table.Td>{a.monthlyMinutes}</Table.Td>
                    <Table.Td>{period(a.startDate, a.endDate)}</Table.Td>
                    {canManage && (
                      <Table.Td>
                        {!a.endDate && (
                          <Button
                            size="compact-sm"
                            variant="subtle"
                            onClick={() =>
                              setDialog({
                                kind: "end",
                                title: t("ohs:services.endAssignment"),
                                minDate: a.startDate,
                                onEnd: (endDate) => endSafetyAssignment(a.id, endDate),
                              })
                            }
                          >
                            {t("ohs:services.end")}
                          </Button>
                        )}
                      </Table.Td>
                    )}
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Stack>

      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={4}>{t("ohs:services.committeeTitle")}</Title>
          {canManage && (
            <Button
              size="sm"
              variant="light"
              leftSection={<Plus size={16} />}
              onClick={() => setDialog({ kind: "member" })}
            >
              {t("ohs:services.addMember")}
            </Button>
          )}
        </Group>
        {committee.isSuccess && memberRows.length === 0 && (
          <Text c="dimmed">{t("ohs:services.noMembers")}</Text>
        )}
        {memberRows.length > 0 && (
          <Table.ScrollContainer minWidth={640}>
            <Table striped verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("ohs:incidents.fields.location")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.committeeRole")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.person")}</Table.Th>
                  <Table.Th>{t("ohs:services.fields.period")}</Table.Th>
                  {canManage && <Table.Th />}
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {memberRows.map((m) => (
                  <Table.Tr key={m.id} opacity={m.endDate ? 0.6 : 1}>
                    <Table.Td>{locations.nameOf(m.locationId)}</Table.Td>
                    <Table.Td>{t(`ohs:services.committeeRoles.${m.role}`)}</Table.Td>
                    <Table.Td>{m.personName}</Table.Td>
                    <Table.Td>{period(m.startDate, m.endDate)}</Table.Td>
                    {canManage && (
                      <Table.Td>
                        {!m.endDate && (
                          <Button
                            size="compact-sm"
                            variant="subtle"
                            onClick={() =>
                              setDialog({
                                kind: "end",
                                title: t("ohs:services.endMember"),
                                minDate: m.startDate,
                                onEnd: (endDate) => endCommitteeMember(m.id, endDate),
                              })
                            }
                          >
                            {t("ohs:services.end")}
                          </Button>
                        )}
                      </Table.Td>
                    )}
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Stack>

      {dialog?.kind === "assignment" && (
        <AssignmentDialog
          locations={locations.options}
          onClose={() => setDialog(null)}
          onSaved={refresh}
        />
      )}
      {dialog?.kind === "member" && (
        <CommitteeMemberDialog
          locations={locations.options}
          onClose={() => setDialog(null)}
          onSaved={refresh}
        />
      )}
      {dialog?.kind === "end" && (
        <EndPeriodDialog
          title={dialog.title}
          minDate={dialog.minDate}
          onEnd={dialog.onEnd}
          onClose={() => setDialog(null)}
          onDone={refresh}
        />
      )}
    </Stack>
  );
}

function LocationCard({ summary, name }: { summary: LocationServiceSummary; name: string }) {
  const { t } = useTranslation(["ohs"]);
  return (
    <Card withBorder padding="md">
      <Stack gap="xs">
        <Group justify="space-between" wrap="nowrap">
          <Text fw={600}>{name}</Text>
          <Badge color={HAZARD_COLORS[summary.hazardClass]} variant="light">
            {t(`ohs:enum.hazardClass.${summary.hazardClass}`)}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          {t("ohs:services.employeeCount", { count: summary.employeeCount })}
        </Text>
        {summary.minutes.map((m) => (
          <Group key={m.role} justify="space-between" wrap="nowrap">
            <Text size="sm">{t(`ohs:services.roles.${m.role}`)}</Text>
            <Text size="sm" c={m.isMet ? "teal" : "red"} fw={500}>
              {t("ohs:services.minutesOf", {
                assigned: m.assignedMinutes,
                required: m.requiredMinutes,
              })}
            </Text>
          </Group>
        ))}
        <Text size="sm">
          {summary.committeeRequired
            ? t("ohs:services.committeeRequired", {
                months: summary.committeeMeetingIntervalMonths,
                members: summary.committeeMemberCount,
                representatives: summary.representativeCount,
                requiredRepresentatives: summary.requiredRepresentativeCount,
              })
            : t("ohs:services.committeeNotRequired")}
        </Text>
        {summary.issues.length === 0 ? (
          <Group gap={6}>
            <CheckCircle2 size={16} color="var(--mantine-color-teal-6)" />
            <Text size="sm" c="teal">
              {t("ohs:services.compliant")}
            </Text>
          </Group>
        ) : (
          <List size="sm" c="red" spacing={2}>
            {summary.issues.map((issue) => (
              <List.Item key={issue}>{t(`ohs:services.issues.${issue}`)}</List.Item>
            ))}
          </List>
        )}
      </Stack>
    </Card>
  );
}
