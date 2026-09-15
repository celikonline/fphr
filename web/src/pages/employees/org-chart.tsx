import { useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Avatar, Button, Card, Group, Paper, Skeleton, Stack, Text, TextInput, Title, Tooltip } from "@mantine/core";
import { ChevronDown, ChevronRight, EyeOff, GitBranch, Search } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { avatarUrl } from "@/components/employees/employee-badges";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { getOrgChart, type OrgChartNode } from "@/services/employees.service";
import { useOrgUnits } from "@/components/employees/use-org-units";

function normalize(value: string): string {
  return value.toLocaleLowerCase("tr").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

function collectIds(nodes: OrgChartNode[], into: Set<string>): Set<string> {
  for (const node of nodes) {
    if (node.children.length > 0) {
      into.add(node.id);
      collectIds(node.children, into);
    }
  }
  return into;
}

/** Ids of matching nodes plus ids of every ancestor that must be open to reveal them. */
function searchTree(nodes: OrgChartNode[], needle: string) {
  const matches = new Set<string>();
  const open = new Set<string>();
  const visit = (node: OrgChartNode): boolean => {
    let hit = normalize(node.displayName).includes(needle);
    if (hit) matches.add(node.id);
    let childHit = false;
    for (const child of node.children) childHit = visit(child) || childHit;
    if (childHit) open.add(node.id);
    hit = hit || childHit;
    return hit;
  };
  nodes.forEach(visit);
  return { matches, open };
}

export default function OrgChartPage() {
  const { t } = useTranslation(["employees", "common"]);
  const canReadEmployees = usePermission("employee.employees.read");
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const { data, isLoading, error } = useQuery({
    queryKey: ["employees", "org-chart"],
    queryFn: getOrgChart,
    retry: (count, err) => getApiErrorStatus(err) !== 403 && count < 1,
  });

  // Fresh chart data opens the top level (adjusted during render, not in an effect).
  const [expandedFor, setExpandedFor] = useState<typeof data>(undefined);
  if (expandedFor !== data) {
    setExpandedFor(data);
    if (data) setExpanded(new Set(data.filter((root) => root.children.length > 0).map((root) => root.id)));
  }

  const needle = normalize(search.trim());
  const found = useMemo(() => (data && needle ? searchTree(data, needle) : null), [data, needle]);

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  if (getApiErrorStatus(error) === 403) {
    return (
      <div className="mx-auto max-w-3xl p-6 md:p-10">
        <Card withBorder padding="xl">
          <Stack align="center" gap="sm" ta="center">
            <EyeOff size={32} color="var(--mantine-color-dimmed)" />
            <Text fw={500}>{t("employees:orgChart.forbiddenTitle")}</Text>
            <Text size="sm" c="dimmed">
              {t("employees:orgChart.forbiddenDescription")}
            </Text>
            <Button component={Link} to="/app" variant="default">
              {t("common:backHome")}
            </Button>
          </Stack>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2">
          <Group component="span" gap="xs">
            <GitBranch size={24} /> {t("employees:orgChart.title")}
          </Group>
        </Title>
        <Text c="dimmed">{t("employees:orgChart.subtitle")}</Text>
      </div>

      <Group gap="xs">
        <TextInput
          className="w-full max-w-sm"
          leftSection={<Search size={16} />}
          placeholder={t("employees:orgChart.searchPlaceholder")}
          aria-label={t("employees:orgChart.searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        {data && data.length > 0 && (
          <>
            <Button type="button" variant="default" size="sm" onClick={() => setExpanded(collectIds(data, new Set()))}>
              {t("employees:orgChart.expandAll")}
            </Button>
            <Button type="button" variant="default" size="sm" onClick={() => setExpanded(new Set())}>
              {t("employees:orgChart.collapseAll")}
            </Button>
          </>
        )}
        {found && (
          <Text size="sm" c="dimmed">
            {t("employees:orgChart.matchCount", { count: found.matches.size })}
          </Text>
        )}
      </Group>

      <Card withBorder padding="md">
        {isLoading ? (
          <Stack gap="xs">
            <Skeleton h={40} w="66%" />
            <Skeleton h={40} w="50%" ml={32} />
            <Skeleton h={40} w="50%" ml={32} />
          </Stack>
        ) : error ? (
          <Text p="lg" ta="center" size="sm" c="red">
            {getApiErrorMessage(error, t("employees:genericError"))}
          </Text>
        ) : !data || data.length === 0 ? (
          <Text p="lg" ta="center" size="sm" c="dimmed">
            {t("employees:orgChart.empty")}
          </Text>
        ) : (
          <ul role="tree" className="space-y-1">
            {data.map((node) => (
              <OrgChartItem
                key={node.id}
                node={node}
                expanded={expanded}
                forcedOpen={found?.open}
                matches={found?.matches}
                onToggle={toggle}
                canOpenCard={canReadEmployees}
              />
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

interface OrgChartItemProps {
  node: OrgChartNode;
  expanded: ReadonlySet<string>;
  forcedOpen?: ReadonlySet<string>;
  matches?: ReadonlySet<string>;
  onToggle: (id: string) => void;
  canOpenCard: boolean;
}

function OrgChartItem({ node, expanded, forcedOpen, matches, onToggle, canOpenCard }: OrgChartItemProps) {
  const { t } = useTranslation(["employees"]);
  const navigate = useNavigate();
  const { nameById } = useOrgUnits();
  const hasChildren = node.children.length > 0;
  const isOpen = hasChildren && (expanded.has(node.id) || Boolean(forcedOpen?.has(node.id)));
  const isMatch = Boolean(matches?.has(node.id));
  const dimmed = matches !== undefined && !isMatch;
  const unitName = node.orgUnitId ? nameById.get(node.orgUnitId) : undefined;
  const [first = "", ...rest] = node.displayName.split(" ");
  const toggleLabel = isOpen ? t("employees:orgChart.collapse") : t("employees:orgChart.expand");

  const content: ReactNode = (
    <Group gap="sm" wrap="nowrap" className="min-w-0">
      <Avatar size={32} radius="xl" src={avatarUrl(node.id)}>
        {`${first.charAt(0)}${rest[rest.length - 1]?.charAt(0) ?? ""}`.toLocaleUpperCase("tr")}
      </Avatar>
      <div className="min-w-0">
        <Text size="sm" fw={500} truncate>
          {node.displayName}
        </Text>
        {(unitName || hasChildren) && (
          <Text size="xs" c="dimmed" truncate>
            {[unitName, hasChildren ? t("employees:orgChart.reports", { count: node.children.length }) : null]
              .filter(Boolean)
              .join(" · ")}
          </Text>
        )}
      </div>
    </Group>
  );

  const nodeStyle = {
    opacity: dimmed ? 0.6 : undefined,
    borderColor: isMatch ? "var(--mantine-primary-color-filled)" : undefined,
    boxShadow: isMatch ? "0 0 0 1px var(--mantine-primary-color-filled)" : undefined,
  };

  return (
    <li role="treeitem" aria-expanded={hasChildren ? isOpen : undefined}>
      <Group gap={4} wrap="nowrap">
        {hasChildren ? (
          <Tooltip label={toggleLabel} openDelay={500}>
            <ActionIcon type="button" variant="subtle" color="gray" aria-label={toggleLabel} onClick={() => onToggle(node.id)}>
              {isOpen ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
            </ActionIcon>
          </Tooltip>
        ) : (
          <div className="w-7 shrink-0" aria-hidden />
        )}
        {canOpenCard ? (
          <Button
            type="button"
            variant={isMatch ? "light" : "default"}
            h="auto"
            px="sm"
            py={8}
            fw={400}
            justify="flex-start"
            className="min-w-0"
            style={nodeStyle}
            onClick={() => navigate(`/app/employees/${node.id}`)}
          >
            {content}
          </Button>
        ) : (
          <Paper
            withBorder
            px="sm"
            py={8}
            className="min-w-0"
            bg={isMatch ? "var(--mantine-primary-color-light)" : undefined}
            style={nodeStyle}
          >
            {content}
          </Paper>
        )}
      </Group>
      {isOpen && (
        <ul role="group" className="ml-3.5 mt-1 space-y-1 border-l pl-4" style={{ borderColor: "var(--mantine-color-default-border)" }}>
          {node.children.map((child) => (
            <OrgChartItem
              key={child.id}
              node={child}
              expanded={expanded}
              forcedOpen={forcedOpen}
              matches={matches}
              onToggle={onToggle}
              canOpenCard={canOpenCard}
            />
          ))}
        </ul>
      )}
    </li>
  );
}
