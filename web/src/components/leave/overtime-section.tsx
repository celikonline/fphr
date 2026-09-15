import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRightLeft, Clock, FileSignature, Plus, Repeat } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime, toDateOnly } from "@/lib/dates";
import type { EmployeeDetail } from "@/services/employees.service";
import {
  convertOvertimeToTimeOff,
  createOvertimeOnBehalf,
  getEmployeeLeaveBalances,
  getEmployeeOvertimes,
  getOvertimeConsents,
  getOvertimeConversions,
  recordOvertimeConsent,
  revokeOvertimeConsent,
  type OvertimeConsent,
  type OvertimeConsentInput,
  type OvertimeConsentSource,
  type OvertimeConversion,
  type OvertimeConversionInput,
  type OvertimeMinutesBasis,
  type OvertimeRequest,
} from "@/services/leave.service";
import { useDays, useLeaveName } from "./leave-shared";
import { OvertimeRequestDialog, OvertimesTable, formatTimeOnly, useMinutes } from "./overtime-shared";

export const OVERTIME_CARD_TAB_PERMISSION = "leave.overtimes.read";

const DEFAULT_RATE = "1.5";
const MIN_RATE = 1;
const MAX_RATE = 3;
const NOTE_MAX_LENGTH = 1000;
const DEFAULT_TYPE = "__default__";

/** Today as a backend DateOnly in the user's local calendar. */
const todayDateOnly = () => toDateOnly(new Date());

/**
 * Employee card "Fazla mesailer": overtime list (add on behalf, convert approved rows to time off), time-off conversions
 * and written overtime consents (leave.overtime_consents.manage).
 */
export function OvertimeSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["leave", "common"]);
  const queryClient = useQueryClient();
  const leaveName = useLeaveName();
  const canRead = usePermission("leave.overtimes.read");
  const canCreate = usePermission("leave.overtimes.create_on_behalf");
  const canConvert = usePermission("leave.overtimes.convert");
  const canOverridePayable = usePermission("leave.overtimes.override_payable");
  const canManageConsents = usePermission("leave.overtime_consents.manage");
  const canReadBalances = usePermission("leave.balances.read");
  const [adding, setAdding] = useState(false);
  const [converting, setConverting] = useState<OvertimeRequest | null>(null);

  const overtimesQuery = useQuery({
    queryKey: ["leave", "overtimes", employee.id],
    queryFn: () => getEmployeeOvertimes(employee.id),
    enabled: canRead,
  });
  const conversionsQuery = useQuery({
    queryKey: ["leave", "overtimes", employee.id, "conversions"],
    queryFn: () => getOvertimeConversions(employee.id),
    enabled: canRead,
    retry: false,
  });
  // Balance (PerYear) types are the ones a conversion can credit; without leave.balances.read the API default is used.
  const balancesQuery = useQuery({
    queryKey: ["leave", "balances", employee.id],
    queryFn: () => getEmployeeLeaveBalances(employee.id),
    enabled: canRead && canReadBalances,
    retry: false,
  });
  const balanceTypes = useMemo(
    () => (balancesQuery.data?.balances ?? []).map((item) => ({ id: item.balance.leaveTypeId, name: leaveName(item.leaveTypeName) })),
    [balancesQuery.data, leaveName],
  );

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["leave"] });

  if (!canRead) {
    return <p className="text-sm text-muted-foreground">{t("leave:overtime.noAccess")}</p>;
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0 pb-3">
          <div className="flex items-center gap-2">
            <Clock className="h-4 w-4 text-muted-foreground" />
            <CardTitle className="text-lg">{t("leave:overtime.cardTitle")}</CardTitle>
          </div>
          {canCreate && (
            <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>
              <Plus className="mr-2 h-4 w-4" /> {t("leave:overtime.addOnBehalf")}
            </Button>
          )}
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {overtimesQuery.isLoading ? (
            <Skeleton className="h-32 w-full" />
          ) : overtimesQuery.isError ? (
            <p className="text-sm text-destructive">{getApiErrorMessage(overtimesQuery.error, t("leave:genericError"))}</p>
          ) : (
            <OvertimesTable
              rows={overtimesQuery.data ?? []}
              actions={
                canConvert
                  ? (row) =>
                      row.status === "approved" && !row.conversionId ? (
                        <div className="flex justify-end">
                          <Button type="button" size="sm" variant="outline" onClick={() => setConverting(row)}>
                            <ArrowRightLeft className="mr-2 h-4 w-4" /> {t("leave:overtime.convert")}
                          </Button>
                        </div>
                      ) : null
                  : undefined
              }
            />
          )}
        </CardContent>
      </Card>

      <ConversionsCard
        isLoading={conversionsQuery.isLoading}
        error={conversionsQuery.isError ? getApiErrorMessage(conversionsQuery.error, t("leave:genericError")) : null}
        rows={conversionsQuery.data ?? []}
        overtimes={overtimesQuery.data ?? []}
        typeName={(id) => balanceTypes.find((type) => type.id === id)?.name ?? null}
      />

      {canManageConsents && <ConsentsCard employeeId={employee.id} />}

      {adding && (
        <OvertimeRequestDialog
          title={t("leave:overtime.addOnBehalf")}
          description={t("leave:overtime.onBehalfDescription", { name: employee.displayName })}
          submitLabel={t("common:save")}
          save={(input) => createOvertimeOnBehalf(employee.id, input)}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:overtime.onBehalfSaved") });
            void refresh();
          }}
        />
      )}

      {converting && (
        <ConvertOvertimeDialog
          employeeId={employee.id}
          overtime={converting}
          allowFull={canOverridePayable}
          typeOptions={balanceTypes}
          onClose={() => setConverting(null)}
          onConverted={() => {
            setConverting(null);
            toast({ variant: "success", title: t("common:success"), description: t("leave:overtime.convertedDone") });
            void refresh();
          }}
        />
      )}
    </div>
  );
}

function ConversionsCard({
  isLoading,
  error,
  rows,
  overtimes,
  typeName,
}: {
  isLoading: boolean;
  error: string | null;
  rows: OvertimeConversion[];
  overtimes: OvertimeRequest[];
  typeName: (id: string) => string | null;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const minutes = useMinutes();
  const days = useDays();
  const overtimeById = useMemo(() => new Map(overtimes.map((row) => [row.id, row])), [overtimes]);
  const sorted = useMemo(() => [...rows].sort((a, b) => b.convertedAt.localeCompare(a.convertedAt)), [rows]);

  return (
    <Card>
      <CardHeader className="flex flex-row items-center gap-2 space-y-0 pb-3">
        <Repeat className="h-4 w-4 text-muted-foreground" />
        <CardTitle className="text-lg">{t("leave:overtime.conversionsTitle")}</CardTitle>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {isLoading ? (
          <Skeleton className="h-24 w-full" />
        ) : error ? (
          <p className="text-sm text-destructive">{error}</p>
        ) : sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:overtime.conversionsEmpty")}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("leave:overtime.convertedAt")}</TableHead>
                <TableHead>{t("leave:overtime.workDate")}</TableHead>
                <TableHead>{t("leave:overtime.basis")}</TableHead>
                <TableHead className="text-right">{t("leave:overtime.rate")}</TableHead>
                <TableHead className="text-right">{t("leave:overtime.basisMinutes")}</TableHead>
                <TableHead className="text-right">{t("leave:overtime.resultMinutes")}</TableHead>
                <TableHead className="text-right">{t("leave:overtime.resultDays")}</TableHead>
                <TableHead>{t("leave:ledger.type")}</TableHead>
                <TableHead>{t("leave:requests.status")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sorted.map((row) => {
                const overtime = overtimeById.get(row.overtimeRequestId);
                return (
                  <TableRow key={row.id}>
                    <TableCell className="whitespace-nowrap text-xs text-muted-foreground">{formatDateTime(row.convertedAt, i18n.language)}</TableCell>
                    <TableCell className="whitespace-nowrap">
                      {overtime ? `${formatDateOnly(overtime.workDate, i18n.language)} ${formatTimeOnly(overtime.startTime)}` : "—"}
                    </TableCell>
                    <TableCell>{t(`leave:overtime.basisOptions.${row.minutesBasis}`)}</TableCell>
                    <TableCell className="text-right">×{days(row.rate)}</TableCell>
                    <TableCell className="whitespace-nowrap text-right">{minutes(row.basisMinutes)}</TableCell>
                    <TableCell className="whitespace-nowrap text-right">{minutes(row.resultMinutes)}</TableCell>
                    <TableCell className="text-right">{days(row.resultDays)}</TableCell>
                    <TableCell>{(row.leaveTypeId && typeName(row.leaveTypeId)) ?? "—"}</TableCell>
                    <TableCell>
                      <Badge variant={row.status === "posted" ? "success" : "outline"}>{t(`leave:overtime.conversionStatus.${row.status}`)}</Badge>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

interface ConvertOvertimeDialogProps {
  employeeId: string;
  overtime: OvertimeRequest;
  allowFull: boolean;
  typeOptions: { id: string; name: string }[];
  onClose: () => void;
  onConverted: () => void;
}

/** "İzne dönüştür": rate (default 1.5), payable/full basis (full needs leave.overtimes.override_payable), target type and note. */
function ConvertOvertimeDialog({ employeeId, overtime, allowFull, typeOptions, onClose, onConverted }: ConvertOvertimeDialogProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const minutes = useMinutes();
  const [rate, setRate] = useState(DEFAULT_RATE);
  const [basis, setBasis] = useState<OvertimeMinutesBasis>("payable");
  const [leaveTypeId, setLeaveTypeId] = useState(DEFAULT_TYPE);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: OvertimeConversionInput) => convertOvertimeToTimeOff(employeeId, overtime.id, input),
    onSuccess: onConverted,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  const parsedRate = Number(rate.replace(",", "."));
  const basisMinutes = basis === "full" ? overtime.durationMinutes : overtime.payableMinutes;
  const rateValid = rate.trim() !== "" && Number.isFinite(parsedRate) && parsedRate >= MIN_RATE && parsedRate <= MAX_RATE;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!rateValid) return setError(t("leave:overtime.validation.rate", { min: MIN_RATE, max: MAX_RATE }));
    setError(null);
    mutation.mutate({
      rate: parsedRate,
      minutesBasis: basis,
      leaveTypeId: leaveTypeId === DEFAULT_TYPE ? null : leaveTypeId,
      note: note.trim() || null,
    });
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("leave:overtime.convertTitle")}</DialogTitle>
          <DialogDescription>
            {t("leave:overtime.convertDescription", {
              date: formatDateOnly(overtime.workDate, i18n.language),
              time: formatTimeOnly(overtime.startTime),
              duration: minutes(overtime.durationMinutes),
              payable: minutes(overtime.payableMinutes),
            })}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label>{t("leave:overtime.basis")}</Label>
            <RadioGroup value={basis} onValueChange={(value) => setBasis(value as OvertimeMinutesBasis)} className="mt-2" disabled={busy}>
              <div className="flex items-center gap-2">
                <RadioGroupItem id="basis-payable" value="payable" />
                <Label htmlFor="basis-payable" className="font-normal">
                  {t("leave:overtime.basisOptions.payable")} ({minutes(overtime.payableMinutes)})
                </Label>
              </div>
              {allowFull && (
                <div className="flex items-center gap-2">
                  <RadioGroupItem id="basis-full" value="full" />
                  <Label htmlFor="basis-full" className="font-normal">
                    {t("leave:overtime.basisOptions.full")} ({minutes(overtime.durationMinutes)})
                  </Label>
                </div>
              )}
            </RadioGroup>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="convert-rate">
                {t("leave:overtime.rate")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input
                id="convert-rate"
                type="number"
                inputMode="decimal"
                min={MIN_RATE}
                max={MAX_RATE}
                step="0.05"
                value={rate}
                disabled={busy}
                onChange={(e) => setRate(e.target.value)}
              />
            </div>
            {typeOptions.length > 0 && (
              <div>
                <Label htmlFor="convert-type">{t("leave:ledger.type")}</Label>
                <Select value={leaveTypeId} onValueChange={setLeaveTypeId} disabled={busy}>
                  <SelectTrigger id="convert-type">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={DEFAULT_TYPE}>{t("leave:overtime.defaultType")}</SelectItem>
                    {typeOptions.map((option) => (
                      <SelectItem key={option.id} value={option.id}>
                        {option.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}
          </div>
          {rateValid && (
            <p className="rounded-md border bg-muted/40 p-3 text-sm" aria-live="polite">
              {t("leave:overtime.convertPreview", { minutes: minutes(Math.round(basisMinutes * parsedRate)) })}
            </p>
          )}
          <div>
            <Label htmlFor="convert-note">{t("leave:overtime.note")}</Label>
            <Textarea id="convert-note" value={note} maxLength={NOTE_MAX_LENGTH} disabled={busy} onChange={(e) => setNote(e.target.value)} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("leave:overtime.convert")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function consentState(consent: OvertimeConsent, today: string): "inEffect" | "revocationScheduled" | "revoked" {
  if (!consent.revocationNoticeAt) return "inEffect";
  return consent.revocationEffectiveAt && consent.revocationEffectiveAt <= today ? "revoked" : "revocationScheduled";
}

/** Written overtime consents: record ("Onay kaydet") and revoke with notice ("Geri al"). */
function ConsentsCard({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const queryClient = useQueryClient();
  const [recording, setRecording] = useState(false);
  const [revoking, setRevoking] = useState<OvertimeConsent | null>(null);
  const query = useQuery({
    queryKey: ["leave", "overtimes", employeeId, "consents"],
    queryFn: () => getOvertimeConsents(employeeId),
    retry: false,
  });
  const today = todayDateOnly();
  const date = (value: string | null | undefined) => (value ? formatDateOnly(value, i18n.language) : "—");
  const sorted = useMemo(() => [...(query.data ?? [])].sort((a, b) => b.givenAt.localeCompare(a.givenAt)), [query.data]);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["leave", "overtimes", employeeId, "consents"] });

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0 pb-3">
        <div className="flex items-center gap-2">
          <FileSignature className="h-4 w-4 text-muted-foreground" />
          <CardTitle className="text-lg">{t("leave:overtime.consentsTitle")}</CardTitle>
        </div>
        <Button type="button" size="sm" variant="outline" onClick={() => setRecording(true)}>
          <Plus className="mr-2 h-4 w-4" /> {t("leave:overtime.recordConsent")}
        </Button>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {query.isLoading ? (
          <Skeleton className="h-24 w-full" />
        ) : query.isError ? (
          <p className="text-sm text-destructive">{getApiErrorMessage(query.error, t("leave:genericError"))}</p>
        ) : sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("leave:overtime.consentsEmpty")}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("leave:overtime.givenAt")}</TableHead>
                <TableHead>{t("leave:overtime.consentSource")}</TableHead>
                <TableHead>{t("leave:overtime.coversNightWork")}</TableHead>
                <TableHead>{t("leave:overtime.revocationNoticeAt")}</TableHead>
                <TableHead>{t("leave:overtime.revocationEffectiveAt")}</TableHead>
                <TableHead>{t("leave:requests.status")}</TableHead>
                <TableHead className="w-0" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {sorted.map((consent) => {
                const state = consentState(consent, today);
                return (
                  <TableRow key={consent.id}>
                    <TableCell className="whitespace-nowrap font-medium">{date(consent.givenAt)}</TableCell>
                    <TableCell>{t(`leave:overtime.consentSources.${consent.source}`)}</TableCell>
                    <TableCell>{consent.coversNightWork ? t("common:yes") : t("common:no")}</TableCell>
                    <TableCell className="whitespace-nowrap">{date(consent.revocationNoticeAt)}</TableCell>
                    <TableCell className="whitespace-nowrap">{date(consent.revocationEffectiveAt)}</TableCell>
                    <TableCell>
                      <Badge variant={state === "inEffect" ? "success" : state === "revoked" ? "outline" : "secondary"}>
                        {t(`leave:overtime.consentStates.${state}`)}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {!consent.revocationNoticeAt && (
                        <Button type="button" size="sm" variant="ghost" onClick={() => setRevoking(consent)}>
                          {t("leave:overtime.revokeConsent")}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>

      {recording && (
        <RecordConsentDialog
          employeeId={employeeId}
          onClose={() => setRecording(false)}
          onSaved={() => {
            setRecording(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:overtime.consentRecorded") });
            void refresh();
          }}
        />
      )}

      {revoking && (
        <RevokeConsentDialog
          employeeId={employeeId}
          consent={revoking}
          onClose={() => setRevoking(null)}
          onRevoked={(updated) => {
            setRevoking(null);
            toast({
              variant: "success",
              title: t("common:success"),
              description: t("leave:overtime.consentRevoked", { date: date(updated.revocationEffectiveAt) }),
            });
            void refresh();
          }}
        />
      )}
    </Card>
  );
}

function RecordConsentDialog({ employeeId, onClose, onSaved }: { employeeId: string; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const [givenAt, setGivenAt] = useState(todayDateOnly());
  const [source, setSource] = useState<OvertimeConsentSource>("employmentContract");
  const [coversNightWork, setCoversNightWork] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: OvertimeConsentInput) => recordOvertimeConsent(employeeId, input),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!givenAt) return setError(t("leave:overtime.validation.dateRequired"));
    setError(null);
    mutation.mutate({ givenAt, source, coversNightWork, documentId: null });
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:overtime.recordConsent")}</DialogTitle>
          <DialogDescription>{t("leave:overtime.recordConsentDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="consent-date">
                {t("leave:overtime.givenAt")}
                <span className="text-destructive"> *</span>
              </Label>
              <DatePicker id="consent-date" value={givenAt} disabled={busy} onChange={setGivenAt} />
            </div>
            <div>
              <Label htmlFor="consent-source">{t("leave:overtime.consentSource")}</Label>
              <Select value={source} onValueChange={(value) => setSource(value as OvertimeConsentSource)} disabled={busy}>
                <SelectTrigger id="consent-source">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="employmentContract">{t("leave:overtime.consentSources.employmentContract")}</SelectItem>
                  <SelectItem value="standaloneForm">{t("leave:overtime.consentSources.standaloneForm")}</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <div className="flex items-start justify-between gap-4 rounded-md border p-3">
            <div>
              <Label htmlFor="consent-night">{t("leave:overtime.coversNightWork")}</Label>
              <p className="mt-1 text-xs text-muted-foreground">{t("leave:overtime.coversNightWorkHint")}</p>
            </div>
            <Switch id="consent-night" checked={coversNightWork} onCheckedChange={setCoversNightWork} disabled={busy} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RevokeConsentDialog({
  employeeId,
  consent,
  onClose,
  onRevoked,
}: {
  employeeId: string;
  consent: OvertimeConsent;
  onClose: () => void;
  onRevoked: (updated: OvertimeConsent) => void;
}) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const [noticeAt, setNoticeAt] = useState(todayDateOnly());
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => revokeOvertimeConsent(employeeId, consent.id, value),
    onSuccess: onRevoked,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!noticeAt) return setError(t("leave:overtime.validation.dateRequired"));
    setError(null);
    mutation.mutate(noticeAt);
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:overtime.revokeTitle")}</DialogTitle>
          <DialogDescription>
            {t("leave:overtime.revokeDescription", { date: formatDateOnly(consent.givenAt, i18n.language) })}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="revoke-notice">
              {t("leave:overtime.revocationNoticeAt")}
              <span className="text-destructive"> *</span>
            </Label>
            <DatePicker id="revoke-notice" value={noticeAt} min={consent.givenAt} disabled={busy} onChange={setNoticeAt} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" variant="destructive" disabled={busy}>
              {busy ? t("common:saving") : t("leave:overtime.revokeConsent")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
