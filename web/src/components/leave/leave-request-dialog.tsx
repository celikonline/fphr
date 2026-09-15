import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { getApiErrorMessage } from "@/lib/api-error";
import type { LeaveRequest, LeaveRequestInput, LeaveRequestPreview, LeaveRequestPreviewInput } from "@/services/leave.service";
import { useDays } from "./leave-shared";

export interface LeaveRequestTypeOption {
  id: string;
  name: string;
  description: string | null;
  requiresDescription: boolean;
  perRequestMaxDays: number | null;
  availableBalance: number | null;
}

interface LeaveRequestDialogProps {
  editing: LeaveRequest | null;
  /** Prefills the form from an existing request without editing it (resubmitting a rejected request). */
  prefill?: LeaveRequest | null;
  types: LeaveRequestTypeOption[];
  title: string;
  description: string;
  submitLabel: string;
  /** Hint under the type select (e.g. only balance types are listed). */
  typesHint?: string;
  /** Cache scope of the preview ("me" or the employee id). */
  previewScope: string;
  /** Live day count; the dialog debounces the date/type changes. */
  preview: (input: LeaveRequestPreviewInput) => Promise<LeaveRequestPreview>;
  /** Saves the request; `bypassApproval` is only sent when `allowBypass` shows the switch. */
  save: (input: LeaveRequestInput, bypassApproval: boolean) => Promise<unknown>;
  allowBypass?: boolean;
  onClose: () => void;
  onSaved: () => void;
}

function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}

/** "+ Talep" / "İzin Ekle": leave type, dates, description, live day count preview and (on behalf) the bypass-approval switch. */
export function LeaveRequestDialog({
  editing,
  prefill = null,
  types,
  title,
  description: dialogDescription,
  submitLabel,
  typesHint,
  previewScope,
  preview,
  save,
  allowBypass = false,
  onClose,
  onSaved,
}: LeaveRequestDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const days = useDays();
  const source = editing ?? prefill;
  const [leaveTypeId, setLeaveTypeId] = useState(source?.leaveTypeId ?? (types.length === 1 ? types[0].id : ""));
  const [startDate, setStartDate] = useState(source?.startDate ?? "");
  const [endDate, setEndDate] = useState(source?.endDate ?? "");
  const [description, setDescription] = useState(source?.description ?? "");
  const [bypassApproval, setBypassApproval] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A pending request's type may be missing from the list (e.g. no longer self-service); keep it selectable.
  const options =
    source && !types.some((type) => type.id === source.leaveTypeId)
      ? [
          ...types,
          {
            id: source.leaveTypeId,
            name: source.leaveTypeName ?? t(`leave:code.${source.leaveTypeCode}`),
            description: null,
            requiresDescription: false,
            perRequestMaxDays: null,
            availableBalance: null,
          },
        ]
      : types;
  const selected = options.find((type) => type.id === leaveTypeId) ?? null;

  const [draftTypeId, draftStart, draftEnd] = useDebounced(`${leaveTypeId}|${startDate}|${endDate}`, 300).split("|");
  const draft = { leaveTypeId: draftTypeId, startDate: draftStart, endDate: draftEnd };
  const canPreview = !!draft.leaveTypeId && !!draft.startDate && !!draft.endDate && draft.endDate >= draft.startDate;
  const previewQuery = useQuery({
    queryKey: ["leave", "preview", previewScope, draft, editing?.id ?? null],
    queryFn: () =>
      preview({ leaveTypeId: draft.leaveTypeId, start: draft.startDate, end: draft.endDate, editedRequestId: editing?.id }),
    enabled: canPreview,
    retry: false,
  });

  const mutation = useMutation({
    mutationFn: (input: LeaveRequestInput) => save(input, allowBypass && bypassApproval),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!leaveTypeId) return setError(t("leave:form.validation.typeRequired"));
    if (!startDate || !endDate) return setError(t("leave:form.validation.datesRequired"));
    if (endDate < startDate) return setError(t("leave:form.validation.range"));
    if (selected?.requiresDescription && !description.trim()) return setError(t("leave:form.validation.descriptionRequired"));
    setError(null);
    mutation.mutate({ leaveTypeId, startDate, endDate, description: description.trim() || null });
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{dialogDescription}</DialogDescription>
        </DialogHeader>
        {options.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:form.noTypes")}</p>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <div>
              <Label htmlFor="leave-type">
                {t("leave:form.type")}
                <span className="text-destructive"> *</span>
              </Label>
              <Select value={leaveTypeId} onValueChange={setLeaveTypeId} disabled={busy}>
                <SelectTrigger id="leave-type">
                  <SelectValue placeholder={t("leave:form.type")} />
                </SelectTrigger>
                <SelectContent>
                  {options.map((type) => (
                    <SelectItem key={type.id} value={type.id}>
                      {type.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {typesHint && <p className="mt-1 text-xs text-muted-foreground">{typesHint}</p>}
              {selected && (
                <div className="mt-2 space-y-1 text-xs text-muted-foreground">
                  {selected.description && <p>{selected.description}</p>}
                  {selected.availableBalance !== null && <p>{t("leave:form.available", { days: days(selected.availableBalance) })}</p>}
                  {selected.perRequestMaxDays !== null && <p>{t("leave:form.perRequestMax", { days: days(selected.perRequestMaxDays) })}</p>}
                </div>
              )}
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="leave-start">
                  {t("leave:form.startDate")}
                  <span className="text-destructive"> *</span>
                </Label>
                <DatePicker id="leave-start" value={startDate} disabled={busy} onChange={setStartDate} />
              </div>
              <div>
                <Label htmlFor="leave-end">
                  {t("leave:form.endDate")}
                  <span className="text-destructive"> *</span>
                </Label>
                <DatePicker id="leave-end" value={endDate} min={startDate || undefined} disabled={busy} onChange={setEndDate} />
              </div>
            </div>
            {canPreview && (
              <div className="rounded-md border bg-muted/40 p-3 text-sm" aria-live="polite">
                {previewQuery.isFetching && !previewQuery.data ? (
                  <p className="text-muted-foreground">{t("leave:form.previewLoading")}</p>
                ) : previewQuery.isError ? (
                  <p className="text-destructive">{getApiErrorMessage(previewQuery.error, t("leave:genericError"))}</p>
                ) : previewQuery.data ? (
                  <>
                    <p>
                      {t("leave:form.previewDays", {
                        days: days(previewQuery.data.days),
                        rest: previewQuery.data.weeklyRestDays,
                        holidays: previewQuery.data.publicHolidayDays,
                      })}
                    </p>
                    {(previewQuery.data.warnings ?? []).map((warning) => (
                      <p key={warning.code} className="mt-1 text-destructive">
                        {warning.message}
                      </p>
                    ))}
                  </>
                ) : null}
              </div>
            )}
            <div>
              <Label htmlFor="leave-description">
                {t("leave:form.description")}
                {selected?.requiresDescription && <span className="text-destructive"> *</span>}
              </Label>
              <Textarea id="leave-description" value={description} disabled={busy} maxLength={2000} onChange={(e) => setDescription(e.target.value)} />
            </div>
            {allowBypass && (
              <div className="flex items-start justify-between gap-4 rounded-md border p-3">
                <div>
                  <Label htmlFor="leave-bypass">{t("leave:form.bypassApproval")}</Label>
                  <p className="mt-1 text-xs text-muted-foreground">{t("leave:form.bypassHint")}</p>
                </div>
                <Switch id="leave-bypass" checked={bypassApproval} onCheckedChange={setBypassApproval} disabled={busy} />
              </div>
            )}
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
