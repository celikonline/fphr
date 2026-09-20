import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
  ActionIcon,
  Anchor,
  Avatar,
  Button,
  Card,
  Divider,
  Group,
  Popover,
  Select,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  ChevronDown,
  ChevronUp,
  EyeOff,
  GitBranch,
  Mail,
  Minus,
  Phone,
  Plus,
  Printer,
  Search,
} from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { avatarColor, EmployeeStatusBadge, initials } from "@/components/employees/employee-badges";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { getEmployee, getOrgChart, type OrgChartNode } from "@/services/employees.service";
import classes from "./org-chart.module.css";

const ALL_COMPANIES = "all";
const ZOOM_STEPS = [0.5, 0.65, 0.8, 1, 1.2];
/** Printable width of a landscape A4 page at 96dpi (12mm margins); wider charts are scaled down to fit. */
const PRINT_WIDTH_PX = 1030;

function normalize(value: string): string {
  return value.toLocaleLowerCase("tr").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

/** Exported for unit testing the expand-depth semantics (H-05); not used outside this module otherwise. */
export function collectIds(
  nodes: OrgChartNode[],
  into: Set<string>,
  maxDepth = Infinity,
  depth = 0
): Set<string> {
  if (depth >= maxDepth) return into;
  for (const node of nodes) {
    if (node.children.length > 0) {
      into.add(node.id);
      collectIds(node.children, into, maxDepth, depth + 1);
    }
  }
  return into;
}

/** Ids of matching nodes plus ids of every ancestor that must be open to reveal them. */
function searchTree(nodes: OrgChartNode[], needle: string) {
  const matches = new Set<string>();
  const open = new Set<string>();
  const visit = (node: OrgChartNode): boolean => {
    let hit =
      normalize(node.displayName).includes(needle) ||
      normalize(node.titleName ?? "").includes(needle);
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

/** Keeps the company's employees; a manager from another company drops out and their matching reports move up to the nearest kept manager. */
function filterByCompany(nodes: OrgChartNode[], companyId: string): OrgChartNode[] {
  return nodes.flatMap((node) => {
    const children = filterByCompany(node.children, companyId);
    return node.companyId === companyId ? [{ ...node, children }] : children;
  });
}

function companyOptions(nodes: OrgChartNode[]) {
  const names = new Map<string, string>();
  let hasUnplaced = false;
  const visit = (node: OrgChartNode) => {
    if (node.companyId) names.set(node.companyId, node.companyName ?? "—");
    else hasUnplaced = true;
    node.children.forEach(visit);
  };
  nodes.forEach(visit);
  const companies = [...names]
    .map(([value, label]) => ({ value, label }))
    .sort((a, b) => a.label.localeCompare(b.label, "tr"));
  return { companies, hasUnplaced };
}

/** Prints only the chart: clones it with the page's styles into a hidden light-scheme iframe. */
async function printChart(tree: HTMLElement, title: string) {
  const frame = document.createElement("iframe");
  frame.style.cssText = "position:fixed;right:0;bottom:0;width:0;height:0;border:0";
  document.body.appendChild(frame);
  const doc = frame.contentDocument;
  const win = frame.contentWindow;
  if (!doc || !win) {
    frame.remove();
    return;
  }

  doc.title = title;
  doc.documentElement.setAttribute("data-mantine-color-scheme", "light");
  const loads: Promise<unknown>[] = [];
  document.querySelectorAll('style, link[rel="stylesheet"]').forEach((node) => {
    const copy = doc.importNode(node, true);
    if (copy instanceof HTMLLinkElement) {
      loads.push(
        new Promise((resolve) => {
          copy.addEventListener("load", resolve, { once: true });
          copy.addEventListener("error", resolve, { once: true });
        })
      );
    }
    doc.head.appendChild(copy);
  });
  const pageStyle = doc.createElement("style");
  pageStyle.textContent =
    "@page{size:landscape;margin:12mm}body{margin:0;background:#fff}[data-print-hide]{display:none!important}";
  doc.head.appendChild(pageStyle);

  const clone = doc.importNode(tree, true);
  doc.body.appendChild(clone);
  await Promise.all(loads);
  // Measured in the iframe, unaffected by the on-screen zoom; `min-width: max-content` keeps the full width.
  clone.style.zoom = String(Math.min(1, PRINT_WIDTH_PX / clone.scrollWidth));
  win.addEventListener("afterprint", () => frame.remove(), { once: true });
  win.focus();
  win.print();
}

export default function OrgChartPage() {
  const { t } = useTranslation(["employees", "common"]);
  const canReadEmployees = usePermission("employee.employees.read");
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [selectedCompany, setSelectedCompany] = useState<string | null>(null);
  const [zoomIndex, setZoomIndex] = useState(ZOOM_STEPS.indexOf(1));
  /** Bumped after "expand all" so the root-centering effect below re-runs even though `roots` itself didn't change. */
  const [centerToken, setCenterToken] = useState(0);
  const treeRef = useRef<HTMLUListElement>(null);
  /** Node DOM ref by id, so a search match can be scrolled into the scrollable chart area. */
  const nodeRefs = useRef<Map<string, HTMLDivElement>>(new Map());
  const registerNode = (id: string, el: HTMLDivElement | null) => {
    if (el) nodeRefs.current.set(id, el);
    else nodeRefs.current.delete(id);
  };

  const { data, isLoading, error } = useQuery({
    queryKey: ["employees", "org-chart"],
    queryFn: getOrgChart,
    retry: (count, err) => getApiErrorStatus(err) !== 403 && count < 1,
  });

  // Fresh chart data opens only the root level (adjusted during render, not in an effect) — a depth-2 default
  // pulled in the whole first department's team, which made the widest possible subtree the initial view and
  // pushed the root off-center (H-04/H-05).
  const [expandedFor, setExpandedFor] = useState<typeof data>(undefined);
  if (expandedFor !== data) {
    setExpandedFor(data);
    if (data) setExpanded(collectIds(data, new Set(), 1));
  }

  const { companies, hasUnplaced } = useMemo(() => companyOptions(data ?? []), [data]);
  const showAllOption = companies.length !== 1 || hasUnplaced;
  const company =
    selectedCompany ?? (showAllOption ? ALL_COMPANIES : (companies[0]?.value ?? ALL_COMPANIES));
  const roots = useMemo(
    () => (data && company !== ALL_COMPANIES ? filterByCompany(data, company) : (data ?? [])),
    [data, company]
  );

  const needle = normalize(search.trim());
  const found = useMemo(() => (needle ? searchTree(roots, needle) : null), [roots, needle]);

  // A fresh search re-opens the matching branch (via forcedOpen) and scrolls the first match into view.
  useEffect(() => {
    if (!found || found.matches.size === 0) return;
    const [firstMatchId] = found.matches;
    const el = firstMatchId ? nodeRefs.current.get(firstMatchId) : undefined;
    el?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
  }, [found]);

  // The tree's flex layout shrinks to its content width, so the root node lands wherever the widest subtree
  // happens to be rather than under the scroll container's center. Re-center it whenever the chart (re)loads,
  // the company filter changes what's rendered, or "Tümünü aç" reflows the width.
  useEffect(() => {
    if (found) return;
    const rootId = roots[0]?.id;
    const el = rootId ? nodeRefs.current.get(rootId) : undefined;
    el?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
  }, [roots, centerToken, found]);

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  if (getApiErrorStatus(error) === 403) {
    return (
      <div className="max-w-3xl p-6 md:p-10">
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

  const zoom = ZOOM_STEPS[zoomIndex];
  const nodes = roots.map((node) => (
    <OrgChartItem
      key={node.id}
      node={node}
      expanded={expanded}
      forcedOpen={found?.open}
      matches={found?.matches}
      onToggle={toggle}
      canOpenCard={canReadEmployees}
      registerNode={registerNode}
    />
  ));

  return (
    <div className="space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2">
            <Group component="span" gap="xs">
              <GitBranch size={24} /> {t("employees:orgChart.title")}
            </Group>
          </Title>
          <Text c="dimmed">{t("employees:orgChart.subtitle")}</Text>
        </div>
        <Button
          type="button"
          leftSection={<Printer size={16} />}
          disabled={roots.length === 0}
          onClick={() =>
            treeRef.current && void printChart(treeRef.current, t("employees:orgChart.title"))
          }
        >
          {t("employees:orgChart.print")}
        </Button>
      </Group>

      <Group gap="xs">
        <TextInput
          className="w-full max-w-sm"
          leftSection={<Search size={16} />}
          placeholder={t("employees:orgChart.searchPlaceholder")}
          aria-label={t("employees:orgChart.searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        {roots.length > 0 && (
          <>
            <Button
              type="button"
              variant="default"
              size="sm"
              onClick={() => {
                setExpanded(collectIds(roots, new Set()));
                setCenterToken((token: number) => token + 1);
              }}
            >
              {t("employees:orgChart.expandAll")}
            </Button>
            <Button
              type="button"
              variant="default"
              size="sm"
              onClick={() => setExpanded(new Set())}
            >
              {t("employees:orgChart.collapseAll")}
            </Button>
            <Group gap={4} wrap="nowrap">
              <ActionIcon
                type="button"
                variant="default"
                aria-label={t("employees:orgChart.zoomOut")}
                disabled={zoomIndex === 0}
                onClick={() => setZoomIndex((i) => i - 1)}
              >
                <Minus size={14} />
              </ActionIcon>
              <Text size="sm" w={44} ta="center" c="dimmed">
                {Math.round(zoom * 100)}%
              </Text>
              <ActionIcon
                type="button"
                variant="default"
                aria-label={t("employees:orgChart.zoomIn")}
                disabled={zoomIndex === ZOOM_STEPS.length - 1}
                onClick={() => setZoomIndex((i) => i + 1)}
              >
                <Plus size={14} />
              </ActionIcon>
            </Group>
          </>
        )}
        {found && (
          <Text size="sm" c="dimmed">
            {t("employees:orgChart.matchCount", { count: found.matches.size })}
          </Text>
        )}
      </Group>

      <Card withBorder padding="lg">
        {isLoading ? (
          <Stack align="center" gap="lg">
            <Skeleton h={36} w={160} />
            <Skeleton h={64} w={64} />
            <Group gap="xl">
              <Skeleton h={64} w={64} />
              <Skeleton h={64} w={64} />
              <Skeleton h={64} w={64} />
            </Group>
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
          <div className="overflow-auto pb-2">
            <div style={{ zoom }}>
              <ul
                ref={treeRef}
                role="tree"
                aria-label={t("employees:orgChart.title")}
                className={classes.tree}
              >
                {companies.length > 0 ? (
                  <li role="none">
                    <Select
                      className={classes.company}
                      variant="unstyled"
                      px="sm"
                      size="sm"
                      fw={500}
                      allowDeselect={false}
                      comboboxProps={{ width: "max-content", position: "bottom-start" }}
                      aria-label={t("employees:orgChart.company")}
                      data={
                        showAllOption
                          ? [
                              { value: ALL_COMPANIES, label: t("employees:orgChart.allCompanies") },
                              ...companies,
                            ]
                          : companies
                      }
                      value={company}
                      onChange={(value) => setSelectedCompany(value)}
                    />
                    {nodes.length > 0 && <ul role="group">{nodes}</ul>}
                  </li>
                ) : (
                  nodes
                )}
              </ul>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}

/** Popover content for a clicked node: fetched lazily so collapsed/unopened nodes never hit the API. */
function OrgChartPersonCard({ employeeId, opened }: { employeeId: string; opened: boolean }) {
  const { t } = useTranslation(["employees"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["employees", "card", employeeId],
    queryFn: () => getEmployee(employeeId),
    enabled: opened,
    staleTime: 60_000,
  });

  if (isLoading) {
    return (
      <Stack gap="xs" miw={220} maw={280}>
        <Group gap="sm" wrap="nowrap">
          <Skeleton height={48} circle />
          <Stack gap={6} flex={1}>
            <Skeleton height={10} width="70%" />
            <Skeleton height={8} width="50%" />
          </Stack>
        </Group>
        <Skeleton height={8} />
        <Skeleton height={8} width="80%" />
      </Stack>
    );
  }

  if (error || !data) {
    return (
      <Text size="sm" c="red" miw={220} maw={280}>
        {t("employees:orgChart.cardError")}
      </Text>
    );
  }

  const contact = data.workEmail
    ? { icon: <Mail size={14} />, value: data.workEmail }
    : data.workPhone || data.mobilePhone
      ? { icon: <Phone size={14} />, value: data.workPhone ?? data.mobilePhone }
      : null;

  return (
    <Stack gap="xs" miw={220} maw={280}>
      <Group gap="sm" wrap="nowrap">
        <Avatar size={48} radius="md" color={avatarColor(data.id)}>
          {initials(data.firstName, data.lastName)}
        </Avatar>
        <div>
          <Text fw={600} size="sm">
            {data.displayName}
          </Text>
          {data.titleName && (
            <Text size="xs" c="dimmed">
              {data.titleName}
            </Text>
          )}
        </div>
      </Group>
      <Group gap={6}>
        <EmployeeStatusBadge status={data.status} />
      </Group>
      <Divider />
      <Stack gap={4}>
        {data.departmentName && (
          <Text size="xs" c="dimmed">
            {data.departmentName}
          </Text>
        )}
        {data.managerDisplayName && (
          <Text size="xs" c="dimmed">
            {t("employees:fields.manager")}: {data.managerDisplayName}
          </Text>
        )}
        {contact && (
          <Group gap={6} wrap="nowrap">
            {contact.icon}
            <Text size="xs" style={{ overflowWrap: "anywhere" }}>
              {contact.value}
            </Text>
          </Group>
        )}
      </Stack>
      <Anchor component={Link} to={`/app/employees/${data.id}`} size="xs">
        {t("employees:orgChart.viewProfile")}
      </Anchor>
    </Stack>
  );
}

interface OrgChartItemProps {
  node: OrgChartNode;
  expanded: ReadonlySet<string>;
  forcedOpen?: ReadonlySet<string>;
  matches?: ReadonlySet<string>;
  onToggle: (id: string) => void;
  canOpenCard: boolean;
  registerNode: (id: string, el: HTMLDivElement | null) => void;
}

function OrgChartItem({
  node,
  expanded,
  forcedOpen,
  matches,
  onToggle,
  canOpenCard,
  registerNode,
}: OrgChartItemProps) {
  const { t } = useTranslation(["employees"]);
  const hasChildren = node.children.length > 0;
  const isOpen = hasChildren && (expanded.has(node.id) || Boolean(forcedOpen?.has(node.id)));
  const isMatch = Boolean(matches?.has(node.id));
  const [first = "", ...rest] = node.displayName.split(" ");
  const toggleLabel = `${isOpen ? t("employees:orgChart.collapse") : t("employees:orgChart.expand")} · ${t("employees:orgChart.reports", { count: node.children.length })}`;
  const [cardOpened, setCardOpened] = useState(false);
  const avatarInitials =
    `${first.charAt(0)}${rest[rest.length - 1]?.charAt(0) ?? ""}`.toLocaleUpperCase("tr");

  return (
    <li
      role="treeitem"
      aria-expanded={hasChildren ? isOpen : undefined}
      aria-label={node.displayName}
    >
      <div
        ref={(el) => registerNode(node.id, el)}
        className={classes.node}
        data-match={isMatch || undefined}
        data-dimmed={(matches !== undefined && !isMatch) || undefined}
      >
        <div className={classes.avatar}>
          {canOpenCard ? (
            <Popover
              opened={cardOpened}
              onChange={setCardOpened}
              position="right-start"
              withArrow
              shadow="md"
              trapFocus
            >
              <Popover.Target>
                <UnstyledButton
                  type="button"
                  className={classes.avatarButton}
                  aria-label={t("employees:orgChart.viewCard", { name: node.displayName })}
                  onClick={() => setCardOpened((o) => !o)}
                >
                  <Avatar size={64} radius="md" fz="lg" color={avatarColor(node.id)}>
                    {avatarInitials}
                  </Avatar>
                </UnstyledButton>
              </Popover.Target>
              <Popover.Dropdown>
                <OrgChartPersonCard employeeId={node.id} opened={cardOpened} />
              </Popover.Dropdown>
            </Popover>
          ) : (
            <Avatar size={64} radius="md" fz="lg" color={avatarColor(node.id)}>
              {avatarInitials}
            </Avatar>
          )}
          {hasChildren && (
            <Tooltip label={toggleLabel} openDelay={400}>
              <ActionIcon
                type="button"
                size="sm"
                radius="xl"
                variant="default"
                className={classes.toggle}
                aria-label={toggleLabel}
                data-print-hide
                onClick={() => onToggle(node.id)}
              >
                {isOpen ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
              </ActionIcon>
            </Tooltip>
          )}
        </div>
        {canOpenCard ? (
          <Anchor
            component={Link}
            to={`/app/employees/${node.id}`}
            className={classes.name}
            underline="hover"
          >
            {node.displayName}
          </Anchor>
        ) : (
          <Text component="span" className={classes.name}>
            {node.displayName}
          </Text>
        )}
        {node.titleName && (
          <Text component="span" className={classes.title}>
            {node.titleName}
          </Text>
        )}
        {node.departmentName && (
          <Text component="span" className={classes.department}>
            {node.departmentName}
          </Text>
        )}
      </div>
      {isOpen && (
        <ul role="group">
          {node.children.map((child) => (
            <OrgChartItem
              key={child.id}
              node={child}
              expanded={expanded}
              forcedOpen={forcedOpen}
              matches={matches}
              onToggle={onToggle}
              canOpenCard={canOpenCard}
              registerNode={registerNode}
            />
          ))}
        </ul>
      )}
    </li>
  );
}
