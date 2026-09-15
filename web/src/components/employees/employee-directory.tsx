import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Anchor, Button, Card, Group, Input, Skeleton, Stack, Table, Text, TextInput, Title } from "@mantine/core";
import { GitBranch, Search, Users } from "lucide-react";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks";
import { getEmployeeDirectory } from "@/services/employees.service";
import OrgUnitSelect from "./org-unit-select";
import { useOrgUnits } from "./use-org-units";

const PAGE_SIZE = 20;

/** Read-only company directory for callers with `employee.directory.view` but not `employee.employees.read`. */
export default function EmployeeDirectory() {
  const { t } = useTranslation(["employees", "common"]);
  const hasOrgChartPermission = usePermission("employee.orgchart.view");
  const orgChartEnabled = useAccessSetting("showOrgChart");
  const canViewOrgChart = hasOrgChartPermission && orgChartEnabled;
  const { nameById } = useOrgUnits();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(search.trim()), 300);
    return () => clearTimeout(timer);
  }, [search]);

  // Back to the first page whenever the effective filters change (adjusted during render, not in an effect).
  const [prevFilters, setPrevFilters] = useState({ debounced, orgUnitId });
  if (prevFilters.debounced !== debounced || prevFilters.orgUnitId !== orgUnitId) {
    setPrevFilters({ debounced, orgUnitId });
    setPage(1);
  }

  const { data, isLoading } = useQuery({
    queryKey: ["employees", "directory", page, debounced, orgUnitId],
    queryFn: () => getEmployeeDirectory({ page, pageSize: PAGE_SIZE, q: debounced || undefined, orgUnitId, sort: "displayName" }),
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2">
            <Group component="span" gap="xs">
              <Users size={24} /> {t("employees:directory.title")}
            </Group>
          </Title>
          <Text c="dimmed">{t("employees:directory.subtitle")}</Text>
        </div>
        {canViewOrgChart && (
          <Button component={Link} to="/app/employees/org-chart" variant="default" leftSection={<GitBranch size={16} />}>
            {t("employees:orgChart.title")}
          </Button>
        )}
      </Group>

      <Group align="flex-end" gap="sm">
        <TextInput
          className="w-full max-w-sm"
          leftSection={<Search size={16} />}
          placeholder={t("employees:searchPlaceholder")}
          aria-label={t("employees:searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        {nameById.size > 0 && (
          <div className="w-64">
            <Input.Label htmlFor="dir-unit" className="sr-only">
              {t("employees:fields.orgUnit")}
            </Input.Label>
            <OrgUnitSelect id="dir-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
          </div>
        )}
      </Group>

      <Card withBorder padding={0}>
        {isLoading ? (
          <Stack gap="xs" p="md">
            <Skeleton h={32} />
            <Skeleton h={32} />
            <Skeleton h={32} />
          </Stack>
        ) : !data || data.items.length === 0 ? (
          <Text p="lg" ta="center" size="sm" c="dimmed">
            {t("employees:empty")}
          </Text>
        ) : (
          <>
            <Table.ScrollContainer minWidth={600}>
              <Table highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("employees:columns.name")}</Table.Th>
                    <Table.Th>{t("employees:columns.orgUnit")}</Table.Th>
                    <Table.Th>{t("employees:columns.workEmail")}</Table.Th>
                    <Table.Th>{t("employees:columns.workPhone")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.items.map((entry) => (
                    <Table.Tr key={entry.id}>
                      <Table.Td fw={500}>{entry.displayName}</Table.Td>
                      <Table.Td>{(entry.orgUnitId && nameById.get(entry.orgUnitId)) || "—"}</Table.Td>
                      <Table.Td>
                        {entry.workEmail ? (
                          <Anchor href={`mailto:${entry.workEmail}`} size="sm" c="inherit">
                            {entry.workEmail}
                          </Anchor>
                        ) : (
                          "—"
                        )}
                      </Table.Td>
                      <Table.Td>{entry.workPhone ?? "—"}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
            <PaginationFooter result={data} onPageChange={setPage} />
          </>
        )}
      </Card>
    </div>
  );
}
