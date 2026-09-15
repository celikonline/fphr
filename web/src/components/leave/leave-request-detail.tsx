import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { CheckCircle2, Circle, CircleDot, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { cancelLeaveRequest, type LeaveApprovalStep, type LeaveRequest, type LeaveRequestDetail } from "@/services/leave.service";
import { Field, LeaveStatusBadge, useDays } from "./leave-shared";

const CANCEL_REASON_MAX_LENGTH = 500;

interface LeaveRequestDetailSheetProps {
  requestId: string;
  /** Cache scope ("me" or the employee id). */
  scope: string;
  load: (id: string) => Promise<LeaveRequestDetail>;
  typeName: (request: LeaveRequest) => string;
  /** Actions for the loaded request (e.g. cancel, resubmit). */
  footer?: (detail: LeaveRequestDetail) => ReactNode;
  onClose: () => void;
}

/** Request detail drawer: dates, days, type, status, description, source, revision, cancellation and the approval chain. */
export function LeaveRequestDetailSheet({ requestId, scope, load, typeName, footer, onClose }: LeaveRequestDetailSheetProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const days = useDays();
  const query = useQuery({ queryKey: ["leave", "request-detail", scope, requestId], queryFn: () => load(requestId), retry: false });
  const detail = query.data;
  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent className="w-full sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{detail ? typeName(detail) : t("leave:detail.title")}</SheetTitle>
          <SheetDescription>{detail ? detail.employeeName : t("leave:detail.description")}</SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-6">
          {query.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : query.isError || !detail ? (
            <p className="text-sm text-destructive">{getApiErrorMessage(query.error, t("leave:genericError"))}</p>
          ) : (
            <>
              <dl className="grid grid-cols-2 gap-4">
                <Field label={t("leave:requests.status")}>
                  <LeaveStatusBadge status={detail.status} />
                </Field>
                <Field label={t("leave:requests.days")}>
                  {days(detail.calculatedDays)} {t("leave:days")}
                </Field>
                <Field label={t("leave:requests.startDate")}>{date(detail.startDate)}</Field>
                <Field label={t("leave:requests.endDate")}>{date(detail.endDate)}</Field>
                <Field label={t("leave:requests.source")}>{t(`leave:requestSource.${detail.source}`)}</Field>
                <Field label={t("leave:detail.revision")}>{detail.revisionNo}</Field>
                <Field label={t("leave:detail.submittedAt")}>{formatDateTime(detail.submittedAt, i18n.language)}</Field>
                <Field label={t("leave:detail.decidedAt")}>{formatDateTime(detail.decidedAt, i18n.language)}</Field>
              </dl>
              <dl className="grid gap-4">
                <Field label={t("leave:requests.description")}>
                  <span className="whitespace-pre-wrap">{detail.description || "—"}</span>
                </Field>
                {detail.previousRequestId && (
                  <p className="text-xs text-muted-foreground">{t("leave:detail.resubmissionOf")}</p>
                )}
              </dl>
              {(detail.status === "cancelled" || detail.cancelledAt) && (
                <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm">
                  <p className="font-medium">
                    {t("leave:detail.cancelledAt", { at: formatDateTime(detail.cancelledAt, i18n.language) })}
                  </p>
                  {detail.cancellationReason && <p className="mt-1 whitespace-pre-wrap text-muted-foreground">{detail.cancellationReason}</p>}
                </div>
              )}
              <Separator />
              <ApprovalChain detail={detail} />
            </>
          )}
        </SheetBody>
        <SheetFooter>
          {detail && footer?.(detail)}
          <Button type="button" variant="outline" onClick={onClose}>
            {t("common:close")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}

function StepIcon({ step, current }: { step: LeaveApprovalStep; current: boolean }) {
  if (step.status === "approved") return <CheckCircle2 className="h-5 w-5 text-emerald-600" />;
  if (step.status === "rejected") return <XCircle className="h-5 w-5 text-destructive" />;
  if (current) return <CircleDot className="h-5 w-5 text-primary" />;
  return <Circle className="h-5 w-5 text-muted-foreground" />;
}

function ApprovalChain({ detail }: { detail: LeaveRequestDetail }) {
  const { t } = useTranslation(["leave"]);
  const approval = detail.approval;

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">{t("leave:approval.title")}</h3>
        {approval && <Badge variant="outline">{t(`leave:approval.status.${approval.status}`, { defaultValue: approval.status })}</Badge>}
      </div>
      {!approval || approval.steps.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("leave:approval.none")}</p>
      ) : (
        <ol className="space-y-2">
          {[...approval.steps]
            .sort((a, b) => a.stepNumber - b.stepNumber)
            .map((step) => {
              const current = approval.status === "pending" && approval.currentStepNumber === step.stepNumber;
              const approver =
                step.approverName ?? (step.fallbackRoleCode ? t("leave:approval.role", { role: step.fallbackRoleCode }) : t("leave:approval.unassigned"));
              return (
                <li
                  key={step.stepNumber}
                  className={cn("flex items-start gap-3 rounded-md border p-3", current && "border-primary bg-primary/5")}
                >
                  <StepIcon step={step} current={current} />
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium">
                      {t("leave:approval.step", { number: step.stepNumber })} · {approver}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {t(`leave:approval.stepStatus.${step.status}`, { defaultValue: step.status })}
                      {" · "}
                      {step.isRequired ? t("leave:approval.required") : t("leave:approval.optional")}
                    </p>
                  </div>
                </li>
              );
            })}
        </ol>
      )}
    </section>
  );
}

interface CancelLeaveRequestDialogProps {
  employeeId: string;
  request: LeaveRequest;
  typeName: (request: LeaveRequest) => string;
  onClose: () => void;
  onCancelled: () => void;
}

/** "İptal et": cancels a pending or approved request with a mandatory reason (leave.requests.cancel_approved). */
export function CancelLeaveRequestDialog({ employeeId, request, typeName, onClose, onCancelled }: CancelLeaveRequestDialogProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: (value: string) => cancelLeaveRequest(employeeId, request.id, value),
    onSuccess: onCancelled,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return setError(t("leave:cancel.reasonRequired"));
    setError(null);
    mutation.mutate(reason.trim());
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:cancel.title")}</DialogTitle>
          <DialogDescription>
            {t(request.status === "approved" ? "leave:cancel.descriptionApproved" : "leave:cancel.descriptionPending", {
              type: typeName(request),
              start: formatDateOnly(request.startDate, i18n.language),
              end: formatDateOnly(request.endDate, i18n.language),
            })}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="cancel-reason">
              {t("leave:cancel.reason")}
              <span className="text-destructive"> *</span>
            </Label>
            <Textarea
              id="cancel-reason"
              value={reason}
              maxLength={CANCEL_REASON_MAX_LENGTH}
              disabled={busy}
              onChange={(e) => setReason(e.target.value)}
            />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" variant="destructive" disabled={busy}>
              {busy ? t("common:saving") : t("leave:cancel.confirm")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
