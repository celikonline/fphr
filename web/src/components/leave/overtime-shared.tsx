import { useCallback, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import type { OvertimeRequest, OvertimeRequestInput, OvertimeRequestStatus, OvertimeSubmission } from "@/services/leave.service";

/** Backend OvertimeLimits.MaxDurationMinutes (one record is at most a day). */
export const OVERTIME_MAX_MINUTES = 24 * 60;
const DESCRIPTION_MAX_LENGTH = 2000;

/** Minutes as "2 sa 30 dk". */
export function useMinutes() {
  const { t } = useTranslation(["leave"]);
  return useCallback(
    (value: number | null | undefined) => {
      if (value === null || value === undefined) return "—";
      const hours = Math.floor(Math.abs(value) / 60);
      const minutes = Math.abs(value) % 60;
      const sign = value < 0 ? "-" : "";
      if (hours === 0) return sign + t("leave:overtime.minutesOnly", { m: minutes });
      if (minutes === 0) return sign + t("leave:overtime.hoursOnly", { h: hours });
      return sign + t("leave:overtime.hoursMinutes", { h: hours, m: minutes });
    },
    [t],
  );
}

/** Submit warnings are resource keys ("leave.warnings.overtime_consent_missing"); unknown keys pass through. */
export function useOvertimeWarning() {
  const { t } = useTranslation(["leave"]);
  return useCallback(
    (code: string) =>
      code.startsWith("leave.warnings.") ? t(`leave:overtime.warnings.${code.slice("leave.warnings.".length)}`, { defaultValue: code }) : code,
    [t],
  );
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
export function formatTimeOnly(value: string): string {
  return value.slice(0, 5);
}

const STATUS_VARIANT: Record<OvertimeRequestStatus, BadgeProps["variant"]> = {
  pending: "secondary",
  approved: "success",
  rejected: "destructive",
  withdrawn: "outline",
  cancelled: "outline",
};

export function OvertimeStatusBadge({ status }: { status: OvertimeRequestStatus }) {
  const { t } = useTranslation(["leave"]);
  return <Badge variant={STATUS_VARIANT[status]}>{t(`leave:status.${status}`)}</Badge>;
}

/** Overtime list (Tarih, Başlangıç, Süre, Ödenecek, Gün türü, Durum, Kaynak, Açıklama); newest work date first. */
export function OvertimesTable({ rows, actions }: { rows: OvertimeRequest[]; actions?: (row: OvertimeRequest) => ReactNode }) {
  const { t, i18n } = useTranslation(["leave"]);
  const minutes = useMinutes();
  const sorted = useMemo(
    () => [...rows].sort((a, b) => b.workDate.localeCompare(a.workDate) || b.startTime.localeCompare(a.startTime)),
    [rows],
  );

  if (sorted.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("leave:overtime.empty")}</p>;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("leave:overtime.workDate")}</TableHead>
          <TableHead>{t("leave:overtime.startTime")}</TableHead>
          <TableHead className="text-right">{t("leave:overtime.duration")}</TableHead>
          <TableHead className="text-right">{t("leave:overtime.payable")}</TableHead>
          <TableHead>{t("leave:overtime.dayKind")}</TableHead>
          <TableHead>{t("leave:requests.status")}</TableHead>
          <TableHead>{t("leave:requests.source")}</TableHead>
          <TableHead>{t("leave:requests.description")}</TableHead>
          {actions && <TableHead className="w-0" />}
        </TableRow>
      </TableHeader>
      <TableBody>
        {sorted.map((row) => (
          <TableRow key={row.id}>
            <TableCell className="whitespace-nowrap font-medium">{formatDateOnly(row.workDate, i18n.language)}</TableCell>
            <TableCell className="whitespace-nowrap">{formatTimeOnly(row.startTime)}</TableCell>
            <TableCell className="whitespace-nowrap text-right">{minutes(row.durationMinutes)}</TableCell>
            <TableCell
              className="whitespace-nowrap text-right"
              title={row.overlapWithScheduleMinutes > 0 ? t("leave:overtime.overlapHint", { value: minutes(row.overlapWithScheduleMinutes) }) : undefined}
            >
              {minutes(row.payableMinutes)}
            </TableCell>
            <TableCell>{t(`leave:overtime.dayKinds.${row.dayKind}`)}</TableCell>
            <TableCell>
              <div className="flex flex-wrap items-center gap-1">
                <OvertimeStatusBadge status={row.status} />
                {row.conversionId && <Badge variant="outline">{t("leave:overtime.converted")}</Badge>}
              </div>
            </TableCell>
            <TableCell>{t(`leave:overtime.sources.${row.source}`)}</TableCell>
            <TableCell className="max-w-[16rem] truncate" title={row.description ?? undefined}>
              {row.description ?? "—"}
            </TableCell>
            {actions && <TableCell>{actions(row)}</TableCell>}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

interface OvertimeRequestDialogProps {
  title: string;
  description: string;
  submitLabel: string;
  save: (input: OvertimeRequestInput) => Promise<OvertimeSubmission>;
  onClose: () => void;
  /** Called after a successful save; with warnings the dialog first shows them and calls this on close. */
  onSaved: (submission: OvertimeSubmission) => void;
}

/** "+ Fazla mesai talebi" / "Fazla mesai ekle": date, start time, duration and description; shows the returned warnings. */
export function OvertimeRequestDialog({ title, description: dialogDescription, submitLabel, save, onClose, onSaved }: OvertimeRequestDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const minutes = useMinutes();
  const warningText = useOvertimeWarning();
  const [workDate, setWorkDate] = useState("");
  const [startTime, setStartTime] = useState("");
  const [hours, setHours] = useState("");
  const [mins, setMins] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submission, setSubmission] = useState<OvertimeSubmission | null>(null);

  const mutation = useMutation({
    mutationFn: save,
    onSuccess: (data) => {
      const normalized = { ...data, warnings: data.warnings ?? [] };
      if (normalized.warnings.length === 0) onSaved(normalized);
      else setSubmission(normalized);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!workDate) return setError(t("leave:overtime.validation.dateRequired"));
    if (!/^\d{2}:\d{2}$/.test(startTime)) return setError(t("leave:overtime.validation.timeRequired"));
    const h = hours.trim() === "" ? 0 : Number(hours);
    const m = mins.trim() === "" ? 0 : Number(mins);
    const total = h * 60 + m;
    if (!Number.isInteger(h) || !Number.isInteger(m) || h < 0 || m < 0 || m > 59 || total < 1 || total > OVERTIME_MAX_MINUTES) {
      return setError(t("leave:overtime.validation.duration", { max: OVERTIME_MAX_MINUTES / 60 }));
    }
    setError(null);
    mutation.mutate({ workDate, startTime: `${startTime}:00`, durationMinutes: total, description: description.trim() || null });
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && (submission ? onSaved(submission) : onClose())}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{dialogDescription}</DialogDescription>
        </DialogHeader>
        {submission ? (
          <div className="space-y-4">
            <p className="text-sm">{t("leave:overtime.savedWithWarnings", { payable: minutes(submission.payableMinutes) })}</p>
            <ul className="space-y-2 rounded-md border border-amber-500/50 bg-amber-50 p-3 text-sm dark:bg-amber-950/40">
              {submission.warnings.map((code) => (
                <li key={code} className="flex items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
                  <span>{warningText(code)}</span>
                </li>
              ))}
            </ul>
            <DialogFooter>
              <Button type="button" onClick={() => onSaved(submission)}>
                {t("common:close")}
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="overtime-date">
                  {t("leave:overtime.workDate")}
                  <span className="text-destructive"> *</span>
                </Label>
                <DatePicker id="overtime-date" value={workDate} disabled={busy} onChange={setWorkDate} />
              </div>
              <div>
                <Label htmlFor="overtime-start">
                  {t("leave:overtime.startTime")}
                  <span className="text-destructive"> *</span>
                </Label>
                <Input id="overtime-start" type="time" step={60} value={startTime} disabled={busy} onChange={(e) => setStartTime(e.target.value.slice(0, 5))} />
              </div>
            </div>
            <div>
              <Label htmlFor="overtime-hours">
                {t("leave:overtime.duration")}
                <span className="text-destructive"> *</span>
              </Label>
              <div className="mt-1 grid grid-cols-2 gap-4">
                <div className="flex items-center gap-2">
                  <Input id="overtime-hours" type="number" min={0} max={24} step={1} value={hours} disabled={busy} onChange={(e) => setHours(e.target.value)} />
                  <span className="text-sm text-muted-foreground">{t("leave:overtime.hoursUnit")}</span>
                </div>
                <div className="flex items-center gap-2">
                  <Input
                    id="overtime-minutes"
                    aria-label={t("leave:overtime.minutesUnit")}
                    type="number"
                    min={0}
                    max={59}
                    step={1}
                    value={mins}
                    disabled={busy}
                    onChange={(e) => setMins(e.target.value)}
                  />
                  <span className="text-sm text-muted-foreground">{t("leave:overtime.minutesUnit")}</span>
                </div>
              </div>
              <p className="mt-1 text-xs text-muted-foreground">{t("leave:overtime.durationHint")}</p>
            </div>
            <div>
              <Label htmlFor="overtime-description">{t("leave:requests.description")}</Label>
              <Textarea
                id="overtime-description"
                value={description}
                maxLength={DESCRIPTION_MAX_LENGTH}
                disabled={busy}
                onChange={(e) => setDescription(e.target.value)}
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={busy}>
                {busy ? t("common:saving") : submitLabel}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
