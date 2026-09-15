import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button, Group, Input, Modal, Paper, Select, Stack, Switch, Text, Textarea } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
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
    <Modal opened onClose={onClose} size="32rem" title={title}>
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
                  </Stack>
                )}
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <Input.Wrapper id="leave-start" label={t("leave:form.startDate")} withAsterisk>
                  <DatePicker id="leave-start" value={startDate} disabled={busy} onChange={setStartDate} />
                </Input.Wrapper>
                <Input.Wrapper id="leave-end" label={t("leave:form.endDate")} withAsterisk>
                  <DatePicker id="leave-end" value={endDate} min={startDate || undefined} disabled={busy} onChange={setEndDate} />
                </Input.Wrapper>
              </div>
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
    </Modal>
  );
}
