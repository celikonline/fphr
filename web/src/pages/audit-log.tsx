import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Download, ScrollText, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import PaginationFooter from "@/components/pagination-footer";
import {
  exportAuditEventsToCsv,
  listAuditActionTypes,
  listAuditActors,
  listAuditEvents,
  type AuditActionType,
} from "@/services/audit.service";

const ALL_VALUE = "__all__";

const ACTION_BADGE: Record<AuditActionType, "default" | "secondary" | "destructive" | "outline" | "success"> = {
  create: "success",
  update: "default",
  delete: "destructive",
  login: "secondary",
  invite: "secondary",
  role_change: "outline",
};

export default function AuditLogPage() {
  const { t, i18n } = useTranslation(["audit", "common"]);

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [actor, setActor] = useState(ALL_VALUE);
  const [action, setAction] = useState(ALL_VALUE);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, actor, action, from, to]);

  const { data, isLoading } = useQuery({
    queryKey: ["audit-events", page, debouncedSearch, actor, action, from, to],
    queryFn: () =>
      listAuditEvents({
        page,
        pageSize: 15,
        q: debouncedSearch || undefined,
        actor: actor === ALL_VALUE ? undefined : actor,
        action: action === ALL_VALUE ? undefined : (action as AuditActionType),
        from: from ? new Date(from).toISOString() : undefined,
        to: to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined,
      }),
  });

  const actors = listAuditActors();
  const actionTypes = listAuditActionTypes();
  const hasFilters = actor !== ALL_VALUE || action !== ALL_VALUE || from !== "" || to !== "" || search !== "";

  function clearFilters() {
    setSearch("");
    setActor(ALL_VALUE);
    setAction(ALL_VALUE);
    setFrom("");
    setTo("");
  }

  function handleExport() {
    if (!data || data.items.length === 0) return;
    const blob = exportAuditEventsToCsv(data.items);
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  function formatTimestamp(value: string) {
    return new Date(value).toLocaleString(i18n.language === "tr" ? "tr-TR" : "en-US");
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <ScrollText className="h-6 w-6" /> {t("audit:title")}
          </h1>
          <p className="text-muted-foreground">{t("audit:subtitle")}</p>
        </div>
        <Button type="button" variant="outline" onClick={handleExport} disabled={!data || data.items.length === 0}>
          <Download className="mr-2 h-4 w-4" /> {t("audit:exportCsv")}
        </Button>
      </div>

      <Card>
        <CardContent className="flex flex-wrap items-end gap-3 p-4">
          <div className="min-w-[14rem] flex-1">
            <Label htmlFor="audit-search">{t("common:search")}</Label>
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="audit-search"
                className="pl-9"
                placeholder={t("audit:searchPlaceholder")}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>
          <div>
            <Label>{t("audit:actor")}</Label>
            <Select value={actor} onValueChange={setActor}>
              <SelectTrigger className="w-56">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_VALUE}>{t("audit:allActors")}</SelectItem>
                {actors.map((a) => (
                  <SelectItem key={a} value={a}>
                    {a}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label>{t("audit:action")}</Label>
            <Select value={action} onValueChange={setAction}>
              <SelectTrigger className="w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_VALUE}>{t("audit:allActions")}</SelectItem>
                {actionTypes.map((a) => (
                  <SelectItem key={a} value={a}>
                    {t(`audit:actionTypes.${a}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label htmlFor="audit-from">{t("audit:from")}</Label>
            <Input id="audit-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="audit-to">{t("audit:to")}</Label>
            <Input id="audit-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          {hasFilters && (
            <Button type="button" variant="ghost" size="sm" onClick={clearFilters}>
              <X className="mr-1 h-4 w-4" /> {t("audit:clearFilters")}
            </Button>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : !data || data.items.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("audit:empty")}</p>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("audit:timestamp")}</TableHead>
                    <TableHead>{t("audit:actor")}</TableHead>
                    <TableHead>{t("audit:action")}</TableHead>
                    <TableHead>{t("audit:resource")}</TableHead>
                    <TableHead>{t("audit:details")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.items.map((event) => (
                    <TableRow key={event.id}>
                      <TableCell className="whitespace-nowrap text-muted-foreground">
                        {formatTimestamp(event.occurredAt)}
                      </TableCell>
                      <TableCell>{event.actor}</TableCell>
                      <TableCell>
                        <Badge variant={ACTION_BADGE[event.action]}>{t(`audit:actionTypes.${event.action}`)}</Badge>
                      </TableCell>
                      <TableCell>{event.resource}</TableCell>
                      <TableCell className="max-w-md truncate text-muted-foreground" title={event.details}>
                        {event.details}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <PaginationFooter result={data} onPageChange={setPage} />
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
