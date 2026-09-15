import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { GitBranch, Search, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Users className="h-6 w-6" /> {t("employees:directory.title")}
          </h1>
          <p className="text-muted-foreground">{t("employees:directory.subtitle")}</p>
        </div>
        {canViewOrgChart && (
          <Button asChild variant="outline">
            <Link to="/app/employees/org-chart">
              <GitBranch className="mr-2 h-4 w-4" /> {t("employees:orgChart.title")}
            </Link>
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="relative w-full max-w-sm">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input className="pl-9" placeholder={t("employees:searchPlaceholder")} value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
        {nameById.size > 0 && (
          <div className="w-64">
            <Label htmlFor="dir-unit" className="sr-only">
              {t("employees:fields.orgUnit")}
            </Label>
            <OrgUnitSelect id="dir-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
          </div>
        )}
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : !data || data.items.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("employees:empty")}</p>
          ) : (
            <>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t("employees:columns.name")}</TableHead>
                      <TableHead>{t("employees:columns.orgUnit")}</TableHead>
                      <TableHead>{t("employees:columns.workEmail")}</TableHead>
                      <TableHead>{t("employees:columns.workPhone")}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {data.items.map((entry) => (
                      <TableRow key={entry.id}>
                        <TableCell className="font-medium">{entry.displayName}</TableCell>
                        <TableCell>{(entry.orgUnitId && nameById.get(entry.orgUnitId)) || "—"}</TableCell>
                        <TableCell>
                          {entry.workEmail ? (
                            <a className="hover:underline" href={`mailto:${entry.workEmail}`}>
                              {entry.workEmail}
                            </a>
                          ) : (
                            "—"
                          )}
                        </TableCell>
                        <TableCell>{entry.workPhone ?? "—"}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
              <PaginationFooter result={data} onPageChange={setPage} />
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
