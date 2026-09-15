import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, ScrollText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { useDays, useLeaveName } from "@/components/leave/leave-shared";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { cn } from "@/lib/utils";
import { cloneLeaveRuleSet, listLeaveRuleSets, type LeaveRuleSet, type LeaveTypeSettings } from "@/services/leave.service";

const RULE_SETS_KEY = ["leave", "rule-sets"] as const;
const NAME_MAX_LENGTH = 200;

/** İzinler → Kurallar: rule sets (default first) and the selected set's leave types; "Kopyala ve düzenle" clones a set. */
export default function LeaveRuleSetsPage() {
  const canRead = usePermission("leave.rule_sets.read");
  if (!canRead) return <NoAccess />;
  return <LeaveRuleSets />;
}

function LeaveRuleSets() {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const canManage = usePermission("leave.rule_sets.manage");
  const query = useQuery({ queryKey: RULE_SETS_KEY, queryFn: listLeaveRuleSets });
  const sets = [...(query.data ?? [])].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || leaveName(a.name).localeCompare(leaveName(b.name)));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = sets.find((set) => set.id === selectedId) ?? sets[0];
  const [cloning, setCloning] = useState<LeaveRuleSet | null>(null);

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
                {canManage && (
                  <Button type="button" size="sm" variant="outline" onClick={() => setCloning(selected)}>
                    <Copy className="mr-2 h-4 w-4" /> {t("leave:ruleSets.clone")}
                  </Button>
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
    </div>
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
