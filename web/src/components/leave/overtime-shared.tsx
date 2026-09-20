import { useCallback, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Alert, Badge, Button, Card, Group, Input, NumberInput, Progress, Radio, Stack, Text, Textarea, type BadgeProps } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { AlertTriangle } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { FormModal } from "@/components/ui/form-modal";
import { TimePicker } from "@/components/ui/time-picker";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import type {
  AnnualOvertimeUsage,
  OvertimePayRateKind,
  OvertimeRequest,
  OvertimeRequestInput,
  OvertimeRequestStatus,
  OvertimeSubmission,
} from "@/services/leave.service";

/** Backend OvertimeLimits.MaxDurationMinutes (one record is at most a day). */
export const OVERTIME_MAX_MINUTES = 24 * 60;
const DESCRIPTION_MAX_LENGTH = 2000;
/** Narrow phones (S-6 mobil kalıbı, bkz. `leave-shared.tsx`'teki `LeaveRequestsMobileList`): kart listesi. */
const MOBILE_QUERY = "(max-width: 390px)";

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

const STATUS_BADGE: Record<OvertimeRequestStatus, Pick<BadgeProps, "variant" | "color">> = {
  pending: { variant: "light" },
  approved: { variant: "filled", color: "green" },
  rejected: { variant: "filled", color: "red" },
  withdrawn: { variant: "outline", color: "gray" },
  cancelled: { variant: "outline", color: "gray" },
};

export function OvertimeStatusBadge({ status }: { status: OvertimeRequestStatus }) {
  const { t } = useTranslation(["leave"]);
  return <Badge {...STATUS_BADGE[status]}>{t(`leave:status.${status}`)}</Badge>;
}

/** İş K. m.41 ücret oranı rozeti (kart senseik-012 §1). */
export function OvertimePayRateBadge({ kind }: { kind: OvertimePayRateKind }) {
  const { t } = useTranslation(["leave"]);
  return (
    <Badge variant="outline" color={kind === "rate50" ? "grape" : "cyan"}>
      {t(`leave:overtime.payRate.${kind}`)}
    </Badge>
  );
}

/**
 * 270 saat yıllık fazla mesai sınırı göstergesi (kart senseik-012 §2, "GetAnnualOvertimeUsageQuery"): "Fazla
 * Mesailerim" sayfası ve onay formunda kullanılır. Provizyon değil, o ana kadarki Pending/Approved talep toplamıdır.
 */
export function AnnualOvertimeUsageGauge({ usage }: { usage: AnnualOvertimeUsage }) {
  const { t } = useTranslation(["leave"]);
  const minutes = useMinutes();
  const percent = usage.limitMinutes > 0 ? Math.min((usage.usedMinutes / usage.limitMinutes) * 100, 100) : 0;

  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs">
        <Text size="sm" fw={500}>
          {t("leave:overtime.annualUsage.title", { year: usage.year })}
        </Text>
        <Text size="sm" c={usage.exceeded ? "red" : "dimmed"}>
          {t("leave:overtime.annualUsage.usedOfLimit", { used: minutes(usage.usedMinutes), limit: minutes(usage.limitMinutes) })}
        </Text>
      </Group>
      <Progress value={percent} color={usage.exceeded ? "red" : percent > 80 ? "yellow" : "blue"} size="md" radius="sm" />
      {usage.exceeded && (
        <Text size="xs" c="red">
          {t("leave:overtime.annualUsage.exceeded")}
        </Text>
      )}
    </Stack>
  );
}

/** Overtime list (Tarih, Başlangıç, Süre, Ödenecek, Gün türü, Durum, Kaynak, Açıklama); newest work date first. */
export function OvertimesTable({ rows, actions }: { rows: OvertimeRequest[]; actions?: (row: OvertimeRequest) => ReactNode }) {
  const { t, i18n } = useTranslation(["leave"]);
  const minutes = useMinutes();
  const isMobile = useMediaQuery(MOBILE_QUERY);
  // Same order as the "workDate" column's accessor (date then start time), newest first.
  const sorted = useMemo(
    () => [...rows].sort((a, b) => `${b.workDate}T${b.startTime}`.localeCompare(`${a.workDate}T${a.startTime}`)),
    [rows],
  );

  const columns: ColumnDef<OvertimeRequest>[] = [
    {
      id: "workDate",
      // Date then start time, so the default newest-first order keeps same-day records in time order.
      accessorFn: (row) => `${row.workDate}T${row.startTime}`,
      header: t("leave:overtime.workDate"),
      meta: { label: t("leave:overtime.workDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => (
        <Text span inherit fw={500}>
          {formatDateOnly(row.original.workDate, i18n.language)}
        </Text>
      ),
    },
    {
      id: "startTime",
      accessorKey: "startTime",
      header: t("leave:overtime.startTime"),
      meta: { label: t("leave:overtime.startTime"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatTimeOnly(row.original.startTime),
    },
    {
      id: "duration",
      accessorKey: "durationMinutes",
      header: t("leave:overtime.duration"),
      meta: { label: t("leave:overtime.duration"), cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => minutes(row.original.durationMinutes),
    },
    {
      id: "payable",
      accessorKey: "payableMinutes",
      header: t("leave:overtime.payable"),
      meta: { label: t("leave:overtime.payable"), cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => (
        <span
          title={
            row.original.overlapWithScheduleMinutes > 0
              ? t("leave:overtime.overlapHint", { value: minutes(row.original.overlapWithScheduleMinutes) })
              : undefined
          }
        >
          {minutes(row.original.payableMinutes)}
        </span>
      ),
    },
    {
      id: "dayKind",
      accessorFn: (row) => t(`leave:overtime.dayKinds.${row.dayKind}`),
      header: t("leave:overtime.dayKind"),
      meta: { label: t("leave:overtime.dayKind") },
    },
    {
      id: "payRateKind",
      accessorKey: "payRateKind",
      header: t("leave:overtime.payRate.label"),
      meta: { label: t("leave:overtime.payRate.label") },
      cell: ({ row }) => <OvertimePayRateBadge kind={row.original.payRateKind} />,
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("leave:requests.status"),
      meta: { label: t("leave:requests.status") },
      cell: ({ row }) => (
        <div className="flex flex-wrap items-center gap-1">
          <OvertimeStatusBadge status={row.original.status} />
          {row.original.conversionId && (
            <Badge variant="outline" color="gray">
              {t("leave:overtime.converted")}
            </Badge>
          )}
        </div>
      ),
    },
    {
      id: "source",
      accessorFn: (row) => t(`leave:overtime.sources.${row.source}`),
      header: t("leave:requests.source"),
      meta: { label: t("leave:requests.source") },
    },
    {
      id: "description",
      enableSorting: false,
      header: t("leave:requests.description"),
      meta: { label: t("leave:requests.description"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) => <span title={row.original.description ?? undefined}>{row.original.description ?? "—"}</span>,
    },
    ...(actions
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            meta: { headerClassName: "w-0" },
            header: () => null,
            cell: ({ row }) => actions(row.original),
          } satisfies ColumnDef<OvertimeRequest>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: rows,
    getRowId: (row) => row.id,
    initialSorting: [{ id: "workDate", desc: true }],
    columnVisibilityKey: "senseik.leave.overtimes.columns",
    pageSize: 20,
  });

  if (rows.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t("leave:overtime.empty")}
      </Text>
    );
  }

  if (isMobile) {
    return <OvertimesMobileList rows={sorted} actions={actions} />;
  }

  return (
    <Stack gap="xs">
      <Group justify="flex-end">
        <DataTableViewOptions table={table} />
      </Group>
      <DataTable table={table} />
    </Stack>
  );
}

/** ≤390px card list mirroring `OvertimesTable`'s columns (S-6 mobil kalıbı, bkz. `LeaveRequestsMobileList`). */
function OvertimesMobileList({ rows, actions }: { rows: OvertimeRequest[]; actions?: (row: OvertimeRequest) => ReactNode }) {
  const { t, i18n } = useTranslation(["leave"]);
  const minutes = useMinutes();

  return (
    <Stack gap="sm">
      {rows.map((row) => (
        <Card key={row.id} withBorder padding="md">
          <Stack gap={4}>
            <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
              <Text size="sm" fw={500}>
                {formatDateOnly(row.workDate, i18n.language)}
              </Text>
              <div className="flex flex-wrap items-center justify-end gap-1">
                <OvertimeStatusBadge status={row.status} />
                {row.conversionId && (
                  <Badge variant="outline" color="gray">
                    {t("leave:overtime.converted")}
                  </Badge>
                )}
              </div>
            </Group>
            <Text size="xs" c="dimmed">
              {formatTimeOnly(row.startTime)} · {minutes(row.durationMinutes)} · {t(`leave:overtime.dayKinds.${row.dayKind}`)}
            </Text>
            <Group gap={4}>
              <OvertimePayRateBadge kind={row.payRateKind} />
            </Group>
            <Text size="xs" c="dimmed">
              {t("leave:overtime.payable")}: {minutes(row.payableMinutes)} · {t(`leave:overtime.sources.${row.source}`)}
            </Text>
            {row.description && (
              <Text size="xs" c="dimmed">
                {row.description}
              </Text>
            )}
            {actions && (
              <Group justify="flex-end" gap={4} wrap="nowrap">
                {actions(row)}
              </Group>
            )}
          </Stack>
        </Card>
      ))}
    </Stack>
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
  const [payRateKind, setPayRateKind] = useState<OvertimePayRateKind>("rate50");
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
    mutation.mutate({ workDate, startTime: `${startTime}:00`, durationMinutes: total, description: description.trim() || null, payRateKind });
  }

  const busy = mutation.isPending;

  return (
    <FormModal opened onClose={() => (submission ? onSaved(submission) : onClose())} title={title} size="lg" closeOnClickOutside={false}>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {dialogDescription}
        </Text>
        {submission ? (
          <>
            <Text size="sm">{t("leave:overtime.savedWithWarnings", { payable: minutes(submission.payableMinutes) })}</Text>
            <Alert color="yellow" variant="light" p="sm">
              <ul className="flex flex-col gap-2">
                {submission.warnings.map((code) => (
                  <li key={code} className="flex items-start gap-2">
                    <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" color="var(--mantine-color-yellow-filled)" />
                    <Text size="sm">{warningText(code)}</Text>
                  </li>
                ))}
              </ul>
            </Alert>
            <Group justify="flex-end">
              <Button type="button" onClick={() => onSaved(submission)}>
                {t("common:close")}
              </Button>
            </Group>
          </>
        ) : (
          <form onSubmit={submit}>
            <Stack gap="md">
              <div className="grid gap-4 sm:grid-cols-2">
                <Input.Wrapper id="overtime-date" label={t("leave:overtime.workDate")} withAsterisk>
                  <DatePicker id="overtime-date" value={workDate} disabled={busy} onChange={setWorkDate} />
                </Input.Wrapper>
                <Input.Wrapper id="overtime-start" label={t("leave:overtime.startTime")} withAsterisk>
                  <TimePicker id="overtime-start" value={startTime} minuteStep={5} disabled={busy} onChange={setStartTime} />
                </Input.Wrapper>
              </div>
              <Input.Wrapper
                id="overtime-hours"
                label={t("leave:overtime.duration")}
                description={t("leave:overtime.durationHint")}
                inputWrapperOrder={["label", "input", "description"]}
                withAsterisk
              >
                <div className="grid grid-cols-2 gap-4">
                  <Group gap="xs" wrap="nowrap">
                    <NumberInput
                      id="overtime-hours"
                      className="flex-1"
                      min={0}
                      max={24}
                      step={1}
                      allowDecimal={false}
                      allowNegative={false}
                      value={hours}
                      disabled={busy}
                      onChange={(value) => setHours(String(value))}
                    />
                    <Text size="sm" c="dimmed">
                      {t("leave:overtime.hoursUnit")}
                    </Text>
                  </Group>
                  <Group gap="xs" wrap="nowrap">
                    <NumberInput
                      id="overtime-minutes"
                      aria-label={t("leave:overtime.minutesUnit")}
                      className="flex-1"
                      min={0}
                      max={59}
                      step={1}
                      allowDecimal={false}
                      allowNegative={false}
                      value={mins}
                      disabled={busy}
                      onChange={(value) => setMins(String(value))}
                    />
                    <Text size="sm" c="dimmed">
                      {t("leave:overtime.minutesUnit")}
                    </Text>
                  </Group>
                </div>
              </Input.Wrapper>
              <Radio.Group
                value={payRateKind}
                onChange={(value) => setPayRateKind(value as OvertimePayRateKind)}
                label={t("leave:overtime.payRate.label")}
                description={t("leave:overtime.payRate.hint")}
              >
                <Group gap="lg" mt={4}>
                  <Radio value="rate50" label={t("leave:overtime.payRate.rate50")} disabled={busy} />
                  <Radio value="rate25" label={t("leave:overtime.payRate.rate25")} disabled={busy} />
                </Group>
              </Radio.Group>
              <Textarea
                id="overtime-description"
                label={t("leave:requests.description")}
                value={description}
                maxLength={DESCRIPTION_MAX_LENGTH}
                disabled={busy}
                autosize
                minRows={3}
                onChange={(e) => setDescription(e.currentTarget.value)}
              />
              {error && (
                <Text size="sm" c="red">
                  {error}
                </Text>
              )}
              <Group justify="flex-end">
                <Button type="button" variant="default" onClick={onClose} disabled={busy}>
                  {t("common:cancel")}
                </Button>
                <Button type="submit" disabled={busy}>
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
