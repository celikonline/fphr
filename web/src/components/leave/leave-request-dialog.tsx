import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Button, Group, Input, Paper, Select, Stack, Switch, Text, Textarea } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { Users } from "lucide-react";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { FormModal } from "@/components/ui/form-modal";
import { TimePicker } from "@/components/ui/time-picker";
import { getApiErrorMessage } from "@/lib/api-error";
import type {
  LeaveHalfDayPeriod,
  LeaveRequest,
  LeaveRequestDetail,
  LeaveRequestInput,
  LeaveRequestPreview,
  LeaveRequestPreviewInput,
  LeaveUnit,
} from "@/services/leave.service";
import { useDays } from "./leave-shared";
import { formatTimeOnly } from "./overtime-shared";

export interface LeaveRequestTypeOption {
  id: string;
  name: string;
  description: string | null;
  requiresDescription: boolean;
  perRequestMaxDays: number | null;
  availableBalance: number | null;
  /** Drives the partial-day fields below; defaults to `fullDay` when the caller cannot look it up (e.g. balance-only fallback). */
  unit?: LeaveUnit;
  /** Type flags (denetim A5): a substitute picker / a "hand in the document" hint show up when set. */
  requiresSubstitute?: boolean;
  requiresDocument?: boolean;
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
  /** The employee the leave is for; never offered as their own substitute. */
  requesterEmployeeId?: string;
  /** Resolves the substitute's name when editing/resubmitting a request that only carries the id. */
  loadDetail?: (requestId: string) => Promise<LeaveRequestDetail>;
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
  requesterEmployeeId,
  loadDetail,
  onClose,
  onSaved,
}: LeaveRequestDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const days = useDays();
  const isMobile = useMediaQuery("(max-width: 390px)");
  const source = editing ?? prefill;
  const [leaveTypeId, setLeaveTypeId] = useState(source?.leaveTypeId ?? (types.length === 1 ? types[0].id : ""));
  const [startDate, setStartDate] = useState(source?.startDate ?? "");
  const [endDate, setEndDate] = useState(source?.endDate ?? "");
  const [description, setDescription] = useState(source?.description ?? "");
  const [startTime, setStartTime] = useState(source?.startTime ? formatTimeOnly(source.startTime) : "");
  const [endTime, setEndTime] = useState(source?.endTime ? formatTimeOnly(source.endTime) : "");
  const [halfDayPeriod, setHalfDayPeriod] = useState<LeaveHalfDayPeriod | "">(source?.halfDayPeriod ?? "");
  const [bypassApproval, setBypassApproval] = useState(false);
  const [substitute, setSubstitute] = useState<PickedEmployee | null>(
    source?.substituteEmployeeId
      ? { id: source.substituteEmployeeId, displayName: (source as Partial<LeaveRequestDetail>).substituteName ?? "" }
      : null,
  );
  const [error, setError] = useState<string | null>(null);
  const substituteNameQuery = useQuery({
    queryKey: ["leave", "request-detail", previewScope, source?.id ?? null],
    queryFn: () => loadDetail!(source!.id),
    enabled: !!loadDetail && !!source && !!substitute && !substitute.displayName,
    retry: false,
  });

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
            unit: "fullDay" as LeaveUnit,
          },
        ]
      : types;
  const selected = options.find((type) => type.id === leaveTypeId) ?? null;
  const unit: LeaveUnit = selected?.unit ?? "fullDay";

  // Hourly/HalfDay are single-day requests (backend rejects start != end for them); keep the end date mirrored.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- mirrors a controlled sibling field, not derivable render-time state
    if (unit !== "fullDay" && endDate !== startDate) setEndDate(startDate);
  }, [unit, startDate, endDate]);

  const [draftTypeId, draftStart, draftEnd, draftStartTime, draftEndTime, draftHalfDayPeriod] = useDebounced(
    `${leaveTypeId}|${startDate}|${endDate}|${startTime}|${endTime}|${halfDayPeriod}`,
    300,
  ).split("|");
  const draft = {
    leaveTypeId: draftTypeId,
    startDate: draftStart,
    endDate: draftEnd,
    startTime: draftStartTime,
    endTime: draftEndTime,
    halfDayPeriod: draftHalfDayPeriod as LeaveHalfDayPeriod | "",
  };
  const canPreview = !!draft.leaveTypeId && !!draft.startDate && !!draft.endDate && draft.endDate >= draft.startDate;
  const previewQuery = useQuery({
    queryKey: ["leave", "preview", previewScope, draft, editing?.id ?? null],
    queryFn: () =>
      preview({
        leaveTypeId: draft.leaveTypeId,
        start: draft.startDate,
        end: draft.endDate,
        editedRequestId: editing?.id,
        startTime: unit === "hourly" && draft.startTime ? `${draft.startTime}:00` : undefined,
        endTime: unit === "hourly" && draft.endTime ? `${draft.endTime}:00` : undefined,
        halfDayPeriod: unit === "halfDay" && draft.halfDayPeriod ? draft.halfDayPeriod : undefined,
      }),
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
    if (unit === "hourly" && (!startTime || !endTime || endTime <= startTime)) return setError(t("leave:form.validation.timeRequired"));
    if (unit === "halfDay" && !halfDayPeriod) return setError(t("leave:form.validation.halfDayRequired"));
    if (selected?.requiresDescription && !description.trim()) return setError(t("leave:form.validation.descriptionRequired"));
    if (selected?.requiresSubstitute && !substitute) return setError(t("leave:form.validation.substituteRequired"));
    setError(null);
    mutation.mutate({
      leaveTypeId,
      startDate,
      endDate: unit === "fullDay" ? endDate : startDate,
      description: description.trim() || null,
      startTime: unit === "hourly" ? `${startTime}:00` : undefined,
      endTime: unit === "hourly" ? `${endTime}:00` : undefined,
      halfDayPeriod: unit === "halfDay" ? (halfDayPeriod as LeaveHalfDayPeriod) : undefined,
      substituteEmployeeId: selected?.requiresSubstitute ? substitute?.id : undefined,
    });
  }

  const busy = mutation.isPending;

  return (
    // senseik-041 Bug 3: this dialog has two date pickers (start/end) whose calendar popovers are portalled outside
    // the modal's own DOM subtree; a stray click meant only to dismiss an open calendar could otherwise land on
    // Mantine's outside-click overlay and silently close the whole form, discarding everything typed so far.
    // closeOnClickOutside={false} keeps the explicit Cancel button / Escape as the only ways to discard the draft.
    <FormModal opened onClose={onClose} size="32rem" title={title} fullScreen={isMobile} closeOnClickOutside={false}>

      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {dialogDescription}
        </Text>
        {options.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("leave:form.noTypes")}
          </Text>
        ) : (
          <form onSubmit={submit}>
            <Stack gap="md">
              <div>
                <Select
                  id="leave-type"
                  label={t("leave:form.type")}
                  withAsterisk
                  placeholder={t("leave:form.type")}
                  description={typesHint}
                  value={leaveTypeId || null}
                  onChange={(value) => setLeaveTypeId(value ?? "")}
                  allowDeselect={false}
                  disabled={busy}
                  data={options.map((type) => ({ value: type.id, label: type.name }))}
                  inputWrapperOrder={["label", "input", "description", "error"]}
                />
                {selected && (
                  <Stack gap={4} mt="xs">
                    {selected.description && (
                      <Text size="xs" c="dimmed">
                        {selected.description}
                      </Text>
                    )}
                    {selected.availableBalance !== null && (
                      <Text size="xs" c="dimmed">
                        {t("leave:form.available", { days: days(selected.availableBalance) })}
                      </Text>
                    )}
                    {selected.perRequestMaxDays !== null && (
                      <Text size="xs" c="dimmed">
                        {t("leave:form.perRequestMax", { days: days(selected.perRequestMaxDays) })}
                      </Text>
                    )}
                    {selected.requiresDocument && (
                      <Text size="xs" c="dimmed">
                        {t("leave:form.documentHint")}
                      </Text>
                    )}
                  </Stack>
                )}
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <Input.Wrapper id="leave-start" label={t("leave:form.startDate")} withAsterisk>
                  <DatePicker id="leave-start" value={startDate} disabled={busy} onChange={setStartDate} />
                </Input.Wrapper>
                {unit === "fullDay" ? (
                  <Input.Wrapper id="leave-end" label={t("leave:form.endDate")} withAsterisk>
                    <DatePicker id="leave-end" value={endDate} min={startDate || undefined} disabled={busy} onChange={setEndDate} />
                  </Input.Wrapper>
                ) : (
                  <Input.Wrapper label={t("leave:form.endDate")}>
                    <Text size="sm" c="dimmed" mt={8}>
                      {t("leave:form.sameDayHint")}
                    </Text>
                  </Input.Wrapper>
                )}
              </div>
              {unit === "halfDay" && (
                <Select
                  id="leave-half-day-period"
                  label={t("leave:form.halfDayPeriod")}
                  withAsterisk
                  placeholder={t("leave:form.halfDayPeriod")}
                  value={halfDayPeriod || null}
                  onChange={(value) => setHalfDayPeriod((value as LeaveHalfDayPeriod) ?? "")}
                  allowDeselect={false}
                  disabled={busy}
                  data={[
                    { value: "morning", label: t("leave:form.halfDayMorning") },
                    { value: "afternoon", label: t("leave:form.halfDayAfternoon") },
                  ]}
                />
              )}
              {unit === "hourly" && (
                <div className="grid gap-4 sm:grid-cols-2">
                  <Input.Wrapper id="leave-hour-start" label={t("leave:form.hourlyStart")} withAsterisk>
                    <TimePicker id="leave-hour-start" value={startTime} disabled={busy} onChange={setStartTime} />
                  </Input.Wrapper>
                  <Input.Wrapper id="leave-hour-end" label={t("leave:form.hourlyEnd")} withAsterisk>
                    <TimePicker id="leave-hour-end" value={endTime} disabled={busy} onChange={setEndTime} />
                  </Input.Wrapper>
                </div>
              )}
              {canPreview && (
                <Paper withBorder p="sm" bg="var(--mantine-color-default-hover)" aria-live="polite">
                  {previewQuery.isFetching && !previewQuery.data ? (
                    <Text size="sm" c="dimmed">
                      {t("leave:form.previewLoading")}
                    </Text>
                  ) : previewQuery.isError ? (
                    <Text size="sm" c="red">
                      {getApiErrorMessage(previewQuery.error, t("leave:genericError"))}
                    </Text>
                  ) : previewQuery.data ? (
                    <>
                      <Text size="sm">
                        {t("leave:form.previewDays", {
                          days: days(previewQuery.data.days),
                          rest: previewQuery.data.weeklyRestDays,
                          holidays: previewQuery.data.publicHolidayDays,
                        })}
                      </Text>
                      {(previewQuery.data.warnings ?? []).map((warning) => (
                        <Text key={warning.code} size="sm" c="red" mt={4}>
                          {warning.message}
                        </Text>
                      ))}
                    </>
                  ) : null}
                </Paper>
              )}
              {canPreview && previewQuery.data?.teamOverlap && previewQuery.data.teamOverlap.count > 0 && (
                <Alert color="blue" variant="light" icon={<Users size={16} />}>
                  <Text size="sm">{t("leave:form.teamOverlap", { count: previewQuery.data.teamOverlap.count })}</Text>
                  {previewQuery.data.teamOverlap.employees.length > 0 && (
                    <Text size="xs" c="dimmed" mt={4}>
                      {previewQuery.data.teamOverlap.employees.map((employee) => employee.name).join(", ")}
                    </Text>
                  )}
                </Alert>
              )}
              {selected?.requiresSubstitute && (
                <EmployeePicker
                  id="leave-substitute"
                  useDirectory
                  label={t("leave:form.substitute")}
                  description={t("leave:form.substituteHint")}
                  placeholder={t("leave:form.substitutePlaceholder")}
                  withAsterisk
                  disabled={busy}
                  excludeId={requesterEmployeeId}
                  value={
                    substitute
                      ? { ...substitute, displayName: substitute.displayName || substituteNameQuery.data?.substituteName || t("leave:form.substitute") }
                      : null
                  }
                  onChange={setSubstitute}
                />
              )}
              <Textarea
                id="leave-description"
                label={t("leave:form.description")}
                withAsterisk={selected?.requiresDescription ?? false}
                value={description}
                disabled={busy}
                maxLength={2000}
                autosize
                minRows={3}
                onChange={(e) => setDescription(e.currentTarget.value)}
              />
              {allowBypass && (
                <Paper withBorder p="sm">
                  <Switch
                    id="leave-bypass"
                    label={t("leave:form.bypassApproval")}
                    description={t("leave:form.bypassHint")}
                    labelPosition="left"
                    checked={bypassApproval}
                    onChange={(e) => setBypassApproval(e.currentTarget.checked)}
                    disabled={busy}
                    styles={{ body: { justifyContent: "space-between" } }}
                  />
                </Paper>
              )}
              {error && (
                <Text size="sm" c="red" role="alert">
                  {error}
                </Text>
              )}
              <Group justify="flex-end" gap="xs">
                <Button type="button" variant="default" onClick={onClose} disabled={busy}>
                  {t("common:cancel")}
                </Button>
                <Button type="submit" loading={busy}>
                  {busy ? t("common:saving") : submitLabel}
                </Button>
              </Group>
            </Stack>
          </form>
        )}
      </Stack>
    </FormModal>
  );
}
