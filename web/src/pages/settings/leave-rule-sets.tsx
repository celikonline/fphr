import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Pencil, ScrollText, Search, Star, Trash2, UserPlus, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { useDays, useLeaveName } from "@/components/leave/leave-shared";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { cn } from "@/lib/utils";
import { listEmployees } from "@/services/employees.service";
import {
  assignLeaveRuleSet,
  cloneLeaveRuleSet,
  deleteLeaveRuleSet,
  listLeaveRuleSets,
  renameLeaveRuleSet,
  setDefaultLeaveRuleSet,
  type LeaveRuleSet,
  type LeaveRuleSetAssignmentResult,
  type LeaveTypeSettings,
} from "@/services/leave.service";

const RULE_SETS_KEY = ["leave", "rule-sets"] as const;
const NAME_MAX_LENGTH = 200;

/**
 * İzinler → Kurallar: rule sets (default first) and the selected set's leave types. Per set: "Kopyala ve düzenle",
 * "Yeniden adlandır", "Varsayılan yap", "Sil" (leave.rule_sets.manage) and "Çalışanlara ata" (leave.rule_sets.assign).
 */
export default function LeaveRuleSetsPage() {
  const canRead = usePermission("leave.rule_sets.read");
  if (!canRead) return <NoAccess />;
  return <LeaveRuleSets />;
}

function LeaveRuleSets() {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const canManage = usePermission("leave.rule_sets.manage");
  const canAssign = usePermission("leave.rule_sets.assign");
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: RULE_SETS_KEY, queryFn: listLeaveRuleSets });
  const sets = [...(query.data ?? [])].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || leaveName(a.name).localeCompare(leaveName(b.name)));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = sets.find((set) => set.id === selectedId) ?? sets[0];
  const [cloning, setCloning] = useState<LeaveRuleSet | null>(null);
  const [renaming, setRenaming] = useState<LeaveRuleSet | null>(null);
  const [assigning, setAssigning] = useState<LeaveRuleSet | null>(null);
  const [deleting, setDeleting] = useState<LeaveRuleSet | null>(null);

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) });

  const setDefaultMutation = useMutation({
    mutationFn: (set: LeaveRuleSet) => setDefaultLeaveRuleSet(set.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.defaultSet") });
    },
    onError,
  });

  const deleteMutation = useMutation({
    mutationFn: (set: LeaveRuleSet) => deleteLeaveRuleSet(set.id),
    onSuccess: async () => {
      setDeleting(null);
      setSelectedId(null);
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.deleted") });
    },
    onError: (error) => {
      setDeleting(null);
      onError(error);
    },
  });

  const deleteBlockedReason = selected
    ? selected.isDefault
      ? t("leave:ruleSets.deleteBlockedDefault")
      : selected.assignedEmployeeCount > 0
        ? t("leave:ruleSets.deleteBlockedEmployees", { count: selected.assignedEmployeeCount })
        : null
    : null;

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <ScrollText className="h-6 w-6" /> {t("leave:ruleSets.title")}
        </h1>
        <p className="text-muted-foreground">{t("leave:ruleSets.subtitle")}</p>
      </div>

      {query.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : query.isError ? (
        <p className="text-sm text-destructive">{getApiErrorMessage(query.error, t("leave:genericError"))}</p>
      ) : sets.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("leave:ruleSets.empty")}</p>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Card className="h-fit">
            <CardContent className="space-y-1 p-2">
              {sets.map((set) => {
                const inactive = set.types.filter((type) => type.settings.isDisabled).length;
                return (
                  <button
                    key={set.id}
                    type="button"
                    onClick={() => setSelectedId(set.id)}
                    className={cn(
                      "w-full rounded-md border-l-4 px-3 py-2 text-left text-sm hover:bg-muted",
                      set.isDefault ? "border-l-primary" : "border-l-transparent",
                      selected?.id === set.id && "bg-muted",
                    )}
                  >
                    <div className="flex flex-wrap items-center gap-1.5 font-medium">
                      {leaveName(set.name)}
                      {set.isDefault && <Badge>{t("leave:ruleSets.default")}</Badge>}
                      {set.isSystemSeed && <Badge variant="outline">{t("leave:ruleSets.systemSeed")}</Badge>}
                    </div>
                    <p className="mt-0.5 text-xs text-muted-foreground">
                      {t("leave:ruleSets.typeCount", { active: set.types.length - inactive, inactive })} ·{" "}
                      {t("leave:ruleSets.assignedCount", { count: set.assignedEmployeeCount })}
                    </p>
                  </button>
                );
              })}
            </CardContent>
          </Card>

          {selected && (
            <Card>
              <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2 space-y-0 pb-3">
                <CardTitle className="text-lg">
                  {leaveName(selected.name)} — {t("leave:ruleSets.typesTitle")}
                </CardTitle>
                {(canManage || canAssign) && (
                  <div className="flex flex-wrap items-center gap-2">
                    {canAssign && (
                      <Button type="button" size="sm" variant="outline" onClick={() => setAssigning(selected)}>
                        <UserPlus className="mr-2 h-4 w-4" /> {t("leave:ruleSets.assign")}
                      </Button>
                    )}
                    {canManage && (
                      <>
                        <Button type="button" size="sm" variant="outline" onClick={() => setRenaming(selected)}>
                          <Pencil className="mr-2 h-4 w-4" /> {t("leave:ruleSets.rename")}
                        </Button>
                        {!selected.isDefault && (
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={setDefaultMutation.isPending}
                            onClick={() => setDefaultMutation.mutate(selected)}
                          >
                            <Star className="mr-2 h-4 w-4" /> {t("leave:ruleSets.makeDefault")}
                          </Button>
                        )}
                        <Button type="button" size="sm" variant="outline" onClick={() => setCloning(selected)}>
                          <Copy className="mr-2 h-4 w-4" /> {t("leave:ruleSets.clone")}
                        </Button>
                        {deleteBlockedReason ? (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              {/* Disabled buttons swallow pointer events; the wrapper carries the tooltip. */}
                              <span tabIndex={0} aria-label={deleteBlockedReason}>
                                <Button type="button" size="sm" variant="outline" disabled className="pointer-events-none">
                                  <Trash2 className="mr-2 h-4 w-4" /> {t("common:delete")}
                                </Button>
                              </span>
                            </TooltipTrigger>
                            <TooltipContent>{deleteBlockedReason}</TooltipContent>
                          </Tooltip>
                        ) : (
                          <Button type="button" size="sm" variant="outline" className="text-destructive" onClick={() => setDeleting(selected)}>
                            <Trash2 className="mr-2 h-4 w-4" /> {t("common:delete")}
                          </Button>
                        )}
                      </>
                    )}
                  </div>
                )}
              </CardHeader>
              <CardContent className="overflow-x-auto">
                {selected.types.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t("leave:ruleSets.noTypes")}</p>
                ) : (
                  <LeaveTypesTable set={selected} />
                )}
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {cloning && (
        <CloneRuleSetDialog
          source={cloning}
          onClose={() => setCloning(null)}
          onCloned={(id) => {
            setCloning(null);
            setSelectedId(id);
          }}
        />
      )}

      {renaming && <RenameRuleSetDialog set={renaming} onClose={() => setRenaming(null)} />}

      {assigning && <AssignRuleSetDialog set={assigning} onClose={() => setAssigning(null)} />}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("leave:ruleSets.deleteTitle")}
        description={t("leave:ruleSets.deleteDescription", { name: deleting ? leaveName(deleting.name) : "" })}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </div>
  );
}

function RenameRuleSetDialog({ set, onClose }: { set: LeaveRuleSet; onClose: () => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [name, setName] = useState(leaveName(set.name).slice(0, NAME_MAX_LENGTH));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => renameLeaveRuleSet(set.id, value),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["leave"] });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.renamed") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("leave:ruleSets.nameRequired"));
    setError(null);
    mutation.mutate(name.trim());
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:ruleSets.renameTitle")}</DialogTitle>
          <DialogDescription>{t("leave:ruleSets.renameDescription", { name: leaveName(set.name) })}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="rule-set-rename">
              {t("leave:ruleSets.name")}
              <span className="text-destructive"> *</span>
            </Label>
            <Input
              id="rule-set-rename"
              value={name}
              maxLength={NAME_MAX_LENGTH}
              disabled={mutation.isPending}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** "Çalışanlara ata": searchable employee multi-select; the result lists the employees that could not be moved. */
function AssignRuleSetDialog({ set, onClose }: { set: LeaveRuleSet; onClose: () => void }) {
  const { t } = useTranslation(["leave", "common", "employees"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [selected, setSelected] = useState<Map<string, string>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<LeaveRuleSetAssignmentResult | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const employeesQuery = useQuery({
    queryKey: ["employees", "rule-set-assign", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 20, q: debounced || undefined, status: "Active", sort: "displayName" }),
    enabled: result === null,
    retry: false,
  });

  const mutation = useMutation({
    mutationFn: (employeeIds: string[]) => assignLeaveRuleSet(set.id, employeeIds),
    onSuccess: async (data) => {
      setResult(data);
      await queryClient.invalidateQueries({ queryKey: ["leave"] });
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  const toggle = (id: string, name: string, checked: boolean) =>
    setSelected((current) => {
      const next = new Map(current);
      if (checked) next.set(id, name);
      else next.delete(id);
      return next;
    });

  const chips = useMemo(() => [...selected.entries()], [selected]);
  const busy = mutation.isPending;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (selected.size === 0) return setError(t("leave:ruleSets.assignNoSelection"));
    setError(null);
    mutation.mutate([...selected.keys()]);
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("leave:ruleSets.assignTitle")}</DialogTitle>
          <DialogDescription>{t("leave:ruleSets.assignDescription", { name: leaveName(set.name) })}</DialogDescription>
        </DialogHeader>
        {result ? (
          <div className="space-y-4">
            <p className="text-sm font-medium">{t("leave:ruleSets.assignedResult", { count: result.assignedCount })}</p>
            {result.warnings.length > 0 && (
              <div className="space-y-2 rounded-md border border-amber-500/50 bg-amber-50 p-3 text-sm dark:bg-amber-950/40">
                <p className="font-medium">{t("leave:ruleSets.assignWarnings", { count: result.warnings.length })}</p>
                <ul className="list-disc space-y-1 pl-5">
                  {result.warnings.map((warning) => (
                    <li key={`${warning.employeeId}-${warning.code}`}>
                      <span className="font-medium">{selected.get(warning.employeeId) ?? warning.employeeId}</span>: {warning.message}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <DialogFooter>
              <Button type="button" onClick={onClose}>
                {t("common:close")}
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            {chips.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {chips.map(([id, name]) => (
                  <Badge key={id} variant="secondary" className="gap-1 pr-1">
                    {name}
                    <button
                      type="button"
                      className="rounded-full p-0.5 hover:bg-background/60"
                      aria-label={t("leave:ruleSets.assignRemove", { name })}
                      onClick={() => toggle(id, name, false)}
                    >
                      <X className="h-3 w-3" />
                    </button>
                  </Badge>
                ))}
              </div>
            )}
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                className="pl-8"
                placeholder={t("employees:picker.search")}
                value={search}
                disabled={busy}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <div className="max-h-72 overflow-y-auto rounded-md border">
              {employeesQuery.isLoading ? (
                <Skeleton className="m-2 h-24" />
              ) : employeesQuery.isError ? (
                <p className="p-3 text-sm text-destructive">{getApiErrorMessage(employeesQuery.error, t("leave:genericError"))}</p>
              ) : (employeesQuery.data?.items ?? []).length === 0 ? (
                <p className="p-3 text-sm text-muted-foreground">{t("employees:picker.empty")}</p>
              ) : (
                (employeesQuery.data?.items ?? []).map((employee) => {
                  const checkboxId = `assign-${employee.id}`;
                  return (
                    <label key={employee.id} htmlFor={checkboxId} className="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm hover:bg-accent">
                      <Checkbox
                        id={checkboxId}
                        checked={selected.has(employee.id)}
                        disabled={busy}
                        onCheckedChange={(checked) => toggle(employee.id, employee.displayName, checked === true)}
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate">{employee.displayName}</span>
                        {employee.workEmail && <span className="block truncate text-xs text-muted-foreground">{employee.workEmail}</span>}
                      </span>
                    </label>
                  );
                })
              )}
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={busy || selected.size === 0}>
                {busy ? t("common:saving") : t("leave:ruleSets.assignSubmit", { count: selected.size })}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}

function limitText(settings: LeaveTypeSettings, t: (key: string) => string, days: (value: number | null | undefined) => string) {
  const amount = settings.limitType === "perYear" ? settings.annualDays : settings.limitType === "perRequest" ? settings.perRequestMaxDays : null;
  const label = t(`leave:limitType.${settings.limitType}`);
  return amount === null || amount === undefined ? label : `${label} (${days(amount)} ${t("leave:days")})`;
}

function LeaveTypesTable({ set }: { set: LeaveRuleSet }) {
  const { t } = useTranslation(["leave"]);
  const leaveName = useLeaveName();
  const days = useDays();

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("leave:ruleSets.col.name")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.code")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.paid")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.limit")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.accrual")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.carryOver")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.unit")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.calendar")}</TableHead>
          <TableHead>{t("leave:ruleSets.col.status")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {set.types.map(({ id, settings }) => {
          const perYear = settings.limitType === "perYear";
          return (
            <TableRow key={id} className={settings.isDisabled ? "text-muted-foreground" : undefined}>
              <TableCell className="font-medium">
                {leaveName(settings.name)}
                {settings.description && <p className="mt-0.5 text-xs font-normal text-muted-foreground">{settings.description}</p>}
              </TableCell>
              <TableCell>{t(`leave:code.${settings.code}`)}</TableCell>
              <TableCell>{settings.isPaid ? t("leave:ruleSets.paid") : t("leave:ruleSets.unpaid")}</TableCell>
              <TableCell>{limitText(settings, t, days)}</TableCell>
              <TableCell>{perYear ? t(`leave:accrualFrequency.${settings.accrualFrequency}`) : "—"}</TableCell>
              <TableCell>
                {perYear
                  ? settings.carryOverMode === "limited" && settings.carryOverMaxDays != null
                    ? `${t("leave:carryOverMode.limited")} (${days(settings.carryOverMaxDays)} ${t("leave:days")})`
                    : t(`leave:carryOverMode.${settings.carryOverMode}`)
                  : "—"}
              </TableCell>
              <TableCell>{t(`leave:unit.${settings.unit}`)}</TableCell>
              <TableCell>{settings.showTypeNameOnCalendar ? t("leave:ruleSets.visible") : t("leave:ruleSets.hidden")}</TableCell>
              <TableCell>
                <Badge variant={settings.isDisabled ? "outline" : "success"}>
                  {settings.isDisabled ? t("leave:ruleSets.disabled") : t("leave:ruleSets.active")}
                </Badge>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function CloneRuleSetDialog({ source, onClose, onCloned }: { source: LeaveRuleSet; onClose: () => void; onCloned: (id: string) => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [name, setName] = useState(`${leaveName(source.name)} ${t("leave:ruleSets.copySuffix")}`.slice(0, NAME_MAX_LENGTH));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => cloneLeaveRuleSet(source.id, value),
    onSuccess: async (id) => {
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.cloned") });
      onCloned(id);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("leave:ruleSets.nameRequired"));
    setError(null);
    mutation.mutate(name.trim());
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:ruleSets.cloneTitle")}</DialogTitle>
          <DialogDescription>{t("leave:ruleSets.cloneDescription", { name: leaveName(source.name) })}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="rule-set-name">
              {t("leave:ruleSets.name")}
              <span className="text-destructive"> *</span>
            </Label>
            <Input
              id="rule-set-name"
              value={name}
              maxLength={NAME_MAX_LENGTH}
              disabled={mutation.isPending}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("leave:ruleSets.clone")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
