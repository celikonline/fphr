import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, EyeOff, GitBranch, Search } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { cn } from "@/lib/utils";
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

  useEffect(() => {
    if (data) setExpanded(new Set(data.filter((root) => root.children.length > 0).map((root) => root.id)));
  }, [data]);

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
        <Card>
          <CardContent className="flex flex-col items-center gap-3 p-10 text-center">
            <EyeOff className="h-8 w-8 text-muted-foreground" />
            <p className="font-medium">{t("employees:orgChart.forbiddenTitle")}</p>
            <p className="text-sm text-muted-foreground">{t("employees:orgChart.forbiddenDescription")}</p>
            <Button asChild variant="outline">
              <Link to="/app">{t("common:backHome")}</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <GitBranch className="h-6 w-6" /> {t("employees:orgChart.title")}
        </h1>
        <p className="text-muted-foreground">{t("employees:orgChart.subtitle")}</p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-sm">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder={t("employees:orgChart.searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        {data && data.length > 0 && (
          <>
            <Button type="button" variant="outline" size="sm" onClick={() => setExpanded(collectIds(data, new Set()))}>
              {t("employees:orgChart.expandAll")}
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setExpanded(new Set())}>
              {t("employees:orgChart.collapseAll")}
            </Button>
          </>
        )}
        {found && (
          <span className="text-sm text-muted-foreground">{t("employees:orgChart.matchCount", { count: found.matches.size })}</span>
        )}
      </div>

      <Card>
        <CardContent className="p-4">
          {isLoading ? (
            <div className="space-y-2">
              <Skeleton className="h-10 w-2/3" />
              <Skeleton className="ml-8 h-10 w-1/2" />
              <Skeleton className="ml-8 h-10 w-1/2" />
            </div>
          ) : error ? (
            <p className="p-6 text-center text-sm text-destructive">{getApiErrorMessage(error, t("employees:genericError"))}</p>
          ) : !data || data.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("employees:orgChart.empty")}</p>
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
        </CardContent>
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

  return (
    <li role="treeitem" aria-expanded={hasChildren ? isOpen : undefined}>
      <div className="flex items-center gap-1">
        <button
          type="button"
          className={cn("flex h-7 w-7 items-center justify-center rounded hover:bg-accent", !hasChildren && "invisible")}
          aria-label={isOpen ? t("employees:orgChart.collapse") : t("employees:orgChart.expand")}
          onClick={() => onToggle(node.id)}
        >
          {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>
        <button
          type="button"
          disabled={!canOpenCard}
          onClick={() => navigate(`/app/employees/${node.id}`)}
          className={cn(
            "flex min-w-0 items-center gap-3 rounded-md border border-border px-3 py-2 text-left transition-colors",
            canOpenCard ? "hover:bg-accent" : "cursor-default",
            isMatch && "border-primary bg-primary/10 ring-1 ring-primary",
            dimmed && "opacity-60"
          )}
        >
          <Avatar className="h-8 w-8">
            <AvatarFallback className="text-xs">{`${first.charAt(0)}${rest[rest.length - 1]?.charAt(0) ?? ""}`.toLocaleUpperCase("tr")}</AvatarFallback>
          </Avatar>
          <span className="min-w-0">
            <span className="block truncate text-sm font-medium">{node.displayName}</span>
            {(unitName || hasChildren) && (
              <span className="block truncate text-xs text-muted-foreground">
                {[unitName, hasChildren ? t("employees:orgChart.reports", { count: node.children.length }) : null]
                  .filter(Boolean)
                  .join(" · ")}
              </span>
            )}
          </span>
        </button>
      </div>
      {isOpen && (
        <ul role="group" className="ml-3.5 mt-1 space-y-1 border-l border-border pl-4">
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
