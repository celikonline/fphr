import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  addCareerRecord,
  deleteCareerRecord,
  getEmployeeCareer,
  getMyCareer,
  updateCareerRecord,
  type CareerChangeType,
  type CareerRecord,
  type CareerRecordInput,
  type EmployeeDetail,
  type EmployeeOrgLevelOption,
  type EmploymentType,
} from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";
import { CAREER_CHANGE_TYPE_OPTIONS, EMPLOYMENT_TYPE_OPTIONS } from "./employee-enum-options";
import { useOrgLevels, useOrgUnits } from "./use-org-units";

const UPDATE_PERMISSION = "employee.employees.update";
const NONE = "__none__";

type Levels = EmployeeOrgLevelOption[];

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function useUnitLookup() {
  const { options, nameById } = useOrgUnits();
  return useMemo(() => {
    const parentById = new Map<string, string | null>(options.map((u) => [u.id, u.parentId]));
    const isDescendantOf = (unitId: string, ancestorId: string) => {
      const seen = new Set<string>();
      let current: string | null | undefined = unitId;
      while (current && !seen.has(current)) {
        if (current === ancestorId) return true;
        seen.add(current);
        current = parentById.get(current);
      }
      return false;
    };
    return { units: options, nameById, isDescendantOf };
  }, [options, nameById]);
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  );
}

/** "Kariyer": current position (per level), history timeline and the "Pozisyon ekle" dialog. */
export function CareerSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const canRead = usePermission("employee.employees.read");
  const canUpdate = usePermission(UPDATE_PERMISSION);
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  const isOwnCard = !canRead && ownEmployeeId === employee.id;
  const editable = canUpdate && !isOwnCard;

  const { levels } = useOrgLevels();
  const { nameById } = useUnitLookup();
  const [dialog, setDialog] = useState<{ mode: "add" } | { mode: "edit"; record: CareerRecord } | null>(null);
  const [deleting, setDeleting] = useState<CareerRecord | null>(null);

  const queryKey = ["employees", "career", employee.id];
  const { data: records = [], isLoading } = useQuery({
    queryKey,
    queryFn: () => (isOwnCard ? getMyCareer() : getEmployeeCareer(employee.id)),
  });

  const onTimelineChanged = (updated: CareerRecord[], message: string) => {
    queryClient.setQueryData(queryKey, updated);
    queryClient.invalidateQueries({ queryKey: ["employees", "detail", employee.id] });
    queryClient.invalidateQueries({ queryKey: ["employees"], exact: false, predicate: (q) => q.queryKey[1] !== "career" });
    toast({ variant: "success", title: t("common:success"), description: message });
  };

  const deleteMutation = useMutation({
    mutationFn: (record: CareerRecord) => deleteCareerRecord(employee.id, record.id),
    onSuccess: (updated) => {
      setDeleting(null);
      onTimelineChanged(updated, t("employees:career.deleted"));
    },
    onError: (error) => {
      setDeleting(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  const current = records.find((r) => r.isCurrent) ?? null;
  const latest = records[0] ?? null;
  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const unitName = (id: string | undefined) => (id ? (nameById.get(id) ?? t("employees:unknownUnit")) : "—");
  const placementSummary = (record: CareerRecord) =>
    levels
      .map((level) => record.assignments.find((a) => a.orgUnitTypeId === level.id))
      .filter((a): a is NonNullable<typeof a> => Boolean(a))
      .map((a) => unitName(a.orgUnitId))
      .join(" / ") || "—";

  if (isLoading) {
    return <Skeleton className="h-64 w-full" />;
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
          <CardTitle className="text-lg">{t("employees:career.currentTitle")}</CardTitle>
          {editable && (
            <Button type="button" size="sm" onClick={() => setDialog({ mode: "add" })}>
              <Plus className="mr-2 h-4 w-4" /> {t("employees:career.addPosition")}
            </Button>
          )}
        </CardHeader>
        <CardContent>
          {!current ? (
            <p className="text-sm text-muted-foreground">{t("employees:career.empty")}</p>
          ) : (
            <dl className="grid gap-4 sm:grid-cols-2">
              {levels.map((level) => (
                <Field key={level.id} label={level.name}>
                  {unitName(current.assignments.find((a) => a.orgUnitTypeId === level.id)?.orgUnitId)}
                </Field>
              ))}
              <Field label={t("employees:fields.manager")}>
                {current.managerEmployeeId ? (
                  <Link className="text-primary hover:underline" to={`/app/employees/${current.managerEmployeeId}`}>
                    {current.managerDisplayName ?? t("employees:unknownEmployee")}
                  </Link>
                ) : (
                  "—"
                )}
              </Field>
              <Field label={t("employees:fields.employmentType")}>
                {current.employmentType ? t(`employees:enum.employmentType.${current.employmentType}`) : "—"}
              </Field>
              <Field label={t("employees:fields.positionStartDate")}>{date(current.validFrom)}</Field>
              <Field label={t("employees:career.changeType")}>{t(`employees:enum.careerChangeType.${current.changeType}`)}</Field>
            </dl>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-lg">{t("employees:career.historyTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          {records.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("employees:career.empty")}</p>
          ) : (
            <ol className="relative space-y-4 border-l border-border pl-5">
              {records.map((record) => {
                const isLatest = record.id === latest?.id;
                return (
                  <li key={record.id} className="relative">
                    <span className={`absolute -left-[26px] top-1.5 h-2.5 w-2.5 rounded-full ${record.isCurrent ? "bg-primary" : "bg-muted-foreground/40"}`} />
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0 space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="text-sm font-medium">
                            {t("employees:career.period", {
                              from: date(record.validFrom),
                              to: record.validTo ? date(record.validTo) : t("employees:career.ongoing"),
                            })}
                          </span>
                          <Badge variant="secondary">{t(`employees:enum.careerChangeType.${record.changeType}`)}</Badge>
                          {record.isCurrent && <Badge>{t("employees:career.current")}</Badge>}
                        </div>
                        <p className="text-sm">{placementSummary(record)}</p>
                        <p className="text-xs text-muted-foreground">
                          {t("employees:fields.manager")}: {record.managerDisplayName ?? "—"}
                          {record.employmentType && ` · ${t(`employees:enum.employmentType.${record.employmentType}`)}`}
                        </p>
                        {record.note && <p className="text-xs text-muted-foreground">{record.note}</p>}
                      </div>
                      {editable && (
                        <div className="flex gap-1">
                          <Button type="button" variant="ghost" size="sm" onClick={() => setDialog({ mode: "edit", record })}>
                            <Pencil className="mr-2 h-4 w-4" /> {t("common:edit")}
                          </Button>
                          {isLatest && records.length > 1 && (
                            <Button type="button" variant="ghost" size="sm" onClick={() => setDeleting(record)}>
                              <Trash2 className="mr-2 h-4 w-4" /> {t("common:delete")}
                            </Button>
                          )}
                        </div>
                      )}
                    </div>
                  </li>
                );
              })}
            </ol>
          )}
        </CardContent>
      </Card>

      {dialog && (
        <CareerRecordDialog
          key={dialog.mode === "edit" ? dialog.record.id : "add"}
          employee={employee}
          levels={levels}
          records={records}
          editing={dialog.mode === "edit" ? dialog.record : null}
          onClose={() => setDialog(null)}
          onSaved={(updated) => {
            setDialog(null);
            onTimelineChanged(updated, t("employees:career.saved"));
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("employees:career.deleteTitle")}
        description={t("employees:career.deleteDescription")}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </div>
  );
}

interface CareerRecordDialogProps {
  employee: EmployeeDetail;
  levels: Levels;
  /** Timeline, newest first. */
  records: CareerRecord[];
  /** Null = "Pozisyon ekle" (prefilled from the current record). */
  editing: CareerRecord | null;
  onClose: () => void;
  onSaved: (updated: CareerRecord[]) => void;
}

function CareerRecordDialog({ employee, levels, records, editing, onClose, onSaved }: CareerRecordDialogProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const { units, isDescendantOf } = useUnitLookup();
  const source = editing ?? records.find((r) => r.isCurrent) ?? records[0] ?? null;
  const latest = records[0] ?? null;
  const isLatest = !editing || editing.id === latest?.id;
  // The record right before the one being edited/added: its start bounds the new start date.
  const previous = editing ? records[records.findIndex((r) => r.id === editing.id) + 1] ?? null : latest;

  const [validFrom, setValidFrom] = useState(editing?.validFrom ?? todayDateOnly());
  const [changeType, setChangeType] = useState<CareerChangeType>(editing?.changeType ?? (records.length === 0 ? "hire" : "promotion"));
  const [employmentType, setEmploymentType] = useState<EmploymentType | null>(source?.employmentType ?? employee.employmentType);
  const [manager, setManager] = useState<PickedEmployee | null>(
    source?.managerEmployeeId ? { id: source.managerEmployeeId, displayName: source.managerDisplayName ?? t("employees:unknownEmployee") } : null
  );
  const [note, setNote] = useState(editing?.note ?? "");
  const [selection, setSelection] = useState<Record<string, string | null>>(() =>
    Object.fromEntries((source?.assignments ?? []).map((a) => [a.orgUnitTypeId, a.orgUnitId]))
  );
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: CareerRecordInput) =>
      editing ? updateCareerRecord(employee.id, editing.id, input) : addCareerRecord(employee.id, input),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  /** Units offered for a level: that level's units, narrowed to the subtree of the nearest choice made above it (when that leaves any). */
  const optionsFor = (levelIndex: number) => {
    const ofLevel = units.filter((u) => u.typeId === levels[levelIndex].id);
    const parentChoice = levels
      .slice(0, levelIndex)
      .map((level) => selection[level.id])
      .filter((id): id is string => Boolean(id))
      .pop();
    if (!parentChoice) return ofLevel;
    const narrowed = ofLevel.filter((u) => isDescendantOf(u.id, parentChoice));
    return narrowed.length > 0 ? narrowed : ofLevel;
  };

  const choose = (levelIndex: number, unitId: string | null) => {
    setSelection((prev) => {
      const next = { ...prev, [levels[levelIndex].id]: unitId };
      // Clear deeper choices that no longer sit under the new unit.
      for (const deeper of levels.slice(levelIndex + 1)) {
        const chosen = next[deeper.id];
        if (unitId && chosen && !isDescendantOf(chosen, unitId)) next[deeper.id] = null;
      }
      return next;
    });
  };

  function submit(event: FormEvent) {
    event.preventDefault();
    const formatted = (value: string) => formatDateOnly(value, i18n.language);
    if (!validFrom) return setError(t("employees:career.validation.validFromRequired"));
    if (previous && validFrom <= previous.validFrom)
      return setError(t("employees:career.validation.validFromAfterPrevious", { date: formatted(previous.validFrom) }));
    if (!previous && employee.hireDate && validFrom < employee.hireDate)
      return setError(t("employees:career.validation.validFromBeforeHire", { date: formatted(employee.hireDate) }));

    setError(null);
    mutation.mutate({
      validFrom,
      changeType,
      employmentType,
      managerEmployeeId: manager?.id ?? null,
      note: note.trim() || null,
      assignments: levels
        .filter((level) => selection[level.id])
        .map((level) => ({ orgUnitTypeId: level.id, orgUnitId: selection[level.id] as string })),
    });
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{editing ? t("employees:career.editPosition") : t("employees:career.addPosition")}</DialogTitle>
          <DialogDescription>{t("employees:career.dialogDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          {levels.length === 0 && <p className="text-sm text-muted-foreground">{t("employees:career.noLevels")}</p>}
          <div className="grid gap-4 sm:grid-cols-2">
            {levels.map((level, index) => {
              const value = selection[level.id] ?? null;
              const options = optionsFor(index);
              const missing = value && !options.some((u) => u.id === value);
              return (
                <div key={level.id}>
                  <Label htmlFor={`career-level-${level.id}`}>{level.name}</Label>
                  <Select value={value ?? NONE} onValueChange={(v) => choose(index, v === NONE ? null : v)} disabled={mutation.isPending}>
                    <SelectTrigger id={`career-level-${level.id}`}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NONE}>{t("employees:career.noUnitSelected")}</SelectItem>
                      {missing && <SelectItem value={value}>{t("employees:unknownUnit")}</SelectItem>}
                      {options.map((unit) => (
                        <SelectItem key={unit.id} value={unit.id}>
                          {unit.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              );
            })}
            <div>
              <Label htmlFor="career-manager">{t("employees:fields.manager")}</Label>
              <EmployeePicker id="career-manager" value={manager} onChange={setManager} excludeId={employee.id} allowClear disabled={mutation.isPending} />
            </div>
            <div>
              <Label htmlFor="career-employmentType">{t("employees:fields.employmentType")}</Label>
              <Select
                value={employmentType ?? NONE}
                onValueChange={(v) => setEmploymentType(v === NONE ? null : (v as EmploymentType))}
                disabled={mutation.isPending}
              >
                <SelectTrigger id="career-employmentType">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("employees:enum.notSet")}</SelectItem>
                  {EMPLOYMENT_TYPE_OPTIONS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {t(`employees:enum.employmentType.${option}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="career-validFrom">
                {t("employees:fields.positionStartDate")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input
                id="career-validFrom"
                type="date"
                value={validFrom}
                disabled={mutation.isPending || !isLatest}
                onChange={(e) => setValidFrom(e.target.value)}
              />
              <p className="mt-1 text-xs text-muted-foreground">
                {isLatest ? t("employees:career.validFromHint") : t("employees:career.validFromLocked")}
              </p>
            </div>
            <div>
              <Label htmlFor="career-changeType">{t("employees:career.changeType")}</Label>
              <Select value={changeType} onValueChange={(v) => setChangeType(v as CareerChangeType)} disabled={mutation.isPending}>
                <SelectTrigger id="career-changeType">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CAREER_CHANGE_TYPE_OPTIONS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {t(`employees:enum.careerChangeType.${option}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <div>
            <Label htmlFor="career-note">{t("employees:career.note")}</Label>
            <Textarea id="career-note" value={note} maxLength={500} disabled={mutation.isPending} onChange={(e) => setNote(e.target.value)} />
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
