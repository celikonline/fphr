import { type FormEvent, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Badge, Button, Card, Group, Modal, SegmentedControl, Skeleton, Stack, Switch, Text, Textarea, Title, Tooltip } from "@mantine/core";
import { CalendarOff, CalendarRange, ChevronLeft, ChevronRight, Moon, Plus } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { TimePicker } from "@/components/ui/time-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import {
  cancelMyAvailability,
  getMyAvailability,
  getMyShiftAssignments,
  submitMyAvailability,
  type AvailabilityKind,
  type EmployeeAvailability,
  type ShiftAssignment,
} from "@/services/attendance.service";

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function endOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
function formatTime(value: string): string {
  return value.slice(0, 5);
}

/** "HH:mm" (or "") → "HH:mm:ss" | null for the API. */
function toApiTime(value: string): string | null {
  return value ? `${value}:00` : null;
}

const AVAILABILITY_KEY = ["attendance", "availability", "me"] as const;
const NOTE_MAX_LENGTH = 500;

/** Vardiyam (11.3.6 self-servis, package A5): the caller's own shift assignments for a month. */
export default function MyShiftsPage() {
  const canView = usePermission("attendance.shift.self");
  if (!canView) return <NoAccess />;
  return <MyShifts />;
}

function MyShifts() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const canReportAvailability = usePermission("attendance.availability.self");
  const queryClient = useQueryClient();
  const [month, setMonth] = useState(() => startOfMonth(new Date()));
  const [reporting, setReporting] = useState(false);
  const [cancelling, setCancelling] = useState<EmployeeAvailability | null>(null);

  const from = toDateOnly(startOfMonth(month));
  const to = toDateOnly(endOfMonth(month));

  const query = useQuery({
    queryKey: ["attendance", "shifts", "me", from, to],
    queryFn: () => getMyShiftAssignments({ from, to }),
  });

  const availabilityQuery = useQuery({
    queryKey: [...AVAILABILITY_KEY, from, to],
    queryFn: () => getMyAvailability({ from, to }),
    enabled: canReportAvailability,
  });

  const invalidateAvailability = () => queryClient.invalidateQueries({ queryKey: AVAILABILITY_KEY });

  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelMyAvailability(id),
    onSuccess: async () => {
      await invalidateAvailability();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:myAvailability.cancelled") });
      setCancelling(null);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("attendance:genericError")) }),
  });

  const monthLabel = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(month),
    [month, i18n.language],
  );

  const sortedShifts = useMemo(
    () => [...(query.data ?? [])].sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)),
    [query.data],
  );

  const activeAvailability = useMemo(
    () =>
      (availabilityQuery.data ?? [])
        .filter((entry) => entry.status === "active")
        .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)),
    [availabilityQuery.data],
  );

  return (
    <div className="max-w-4xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarRange className="h-6 w-6" /> {t("attendance:my.shiftTitle")}
          </Title>
          <Text c="dimmed">{t("attendance:my.shiftSubtitle")}</Text>
        </div>
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance:previousMonth")}>
            <ActionIcon variant="default" aria-label={t("attendance:previousMonth")} onClick={() => setMonth((m) => new Date(m.getFullYear(), m.getMonth() - 1, 1))}>
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} tt="capitalize" miw={140} ta="center">
            {monthLabel}
          </Text>
          <Tooltip label={t("attendance:nextMonth")}>
            <ActionIcon variant="default" aria-label={t("attendance:nextMonth")} onClick={() => setMonth((m) => new Date(m.getFullYear(), m.getMonth() + 1, 1))}>
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <Card withBorder padding="lg">
        {query.isLoading ? (
          <Stack gap="xs">
            <Skeleton h={56} />
            <Skeleton h={56} />
            <Skeleton h={56} />
          </Stack>
        ) : query.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(query.error, t("attendance:genericError"))}
          </Text>
        ) : sortedShifts.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("attendance:shift.empty")}
          </Text>
        ) : (
          <Stack gap="xs">
            {sortedShifts.map((shift) => (
              <ShiftRow
                key={shift.id}
                shift={shift}
                formatDate={(value) => formatDateOnly(value, i18n.language, { weekday: "short", day: "2-digit", month: "short" })}
              />
            ))}
          </Stack>
        )}
      </Card>

      {canReportAvailability && (
        <>
          <Group justify="space-between" align="center" gap="sm">
            <Title order={2} size="h4" className="flex items-center gap-2">
              <CalendarOff className="h-5 w-5" /> {t("attendance:myAvailability.title")}
            </Title>
            <Button size="xs" leftSection={<Plus className="h-4 w-4" />} onClick={() => setReporting(true)}>
              {t("attendance:myAvailability.addButton")}
            </Button>
          </Group>

          <Card withBorder padding="lg">
            {availabilityQuery.isLoading ? (
              <Stack gap="xs">
                <Skeleton h={56} />
                <Skeleton h={56} />
              </Stack>
            ) : availabilityQuery.isError ? (
              <Text size="sm" c="red">
                {getApiErrorMessage(availabilityQuery.error, t("attendance:genericError"))}
              </Text>
            ) : activeAvailability.length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("attendance:myAvailability.empty")}
              </Text>
            ) : (
              <Stack gap="xs">
                {activeAvailability.map((entry) => (
                  <AvailabilityRow
                    key={entry.id}
                    entry={entry}
                    formatDate={(value) => formatDateOnly(value, i18n.language, { weekday: "short", day: "2-digit", month: "short" })}
                    onCancel={() => setCancelling(entry)}
                  />
                ))}
              </Stack>
            )}
          </Card>
        </>
      )}

      {reporting && <ReportAvailabilityDialog onClose={() => setReporting(false)} onSubmitted={invalidateAvailability} />}

      <ConfirmDialog
        open={cancelling !== null}
        onOpenChange={(open) => !open && setCancelling(null)}
        title={t("attendance:myAvailability.cancelTitle")}
        description={
          cancelling
            ? t("attendance:myAvailability.cancelDescription", {
                date: formatDateOnly(cancelling.date, i18n.language, { day: "2-digit", month: "long", year: "numeric" }),
              })
            : ""
        }
        confirmLabel={t("attendance:myAvailability.cancel")}
        isLoading={cancelMutation.isPending}
        onConfirm={() => cancelling && cancelMutation.mutate(cancelling.id)}
      />
    </div>
  );
}

function AvailabilityRow({
  entry,
  formatDate,
  onCancel,
}: {
  entry: EmployeeAvailability;
  formatDate: (value: string) => string;
  onCancel: () => void;
}) {
  const { t } = useTranslation(["attendance"]);

  return (
    <div className="flex flex-col gap-2 rounded-md border border-[var(--mantine-color-default-border)] p-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <Group gap={6} wrap="wrap">
          <Text fw={500} tt="capitalize">
            {formatDate(entry.date)}
          </Text>
          <Badge variant="light" color={entry.kind === "weeklyRestRequest" ? "grape" : "orange"}>
            {t(`attendance:myAvailability.kind.${entry.kind}`)}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          {entry.start && entry.end ? `${formatTime(entry.start)} – ${formatTime(entry.end)}` : t("attendance:myAvailability.allDay")}
        </Text>
        {entry.note && (
          <Text size="sm" c="dimmed">
            {entry.note}
          </Text>
        )}
      </div>
      <Button variant="subtle" color="red" size="xs" onClick={onCancel}>
        {t("attendance:myAvailability.cancel")}
      </Button>
    </div>
  );
}

/** "Müsait değilim bildir" (package A5, 11.3.6). */
function ReportAvailabilityDialog({ onClose, onSubmitted }: { onClose: () => void; onSubmitted: () => void }) {
  const { t } = useTranslation(["attendance", "common"]);
  const today = toDateOnly(new Date());
  const [date, setDate] = useState(today);
  const [kind, setKind] = useState<AvailabilityKind>("unavailable");
  const [allDay, setAllDay] = useState(true);
  const [start, setStart] = useState("09:00");
  const [end, setEnd] = useState("18:00");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      submitMyAvailability({
        date,
        start: allDay ? null : toApiTime(start),
        end: allDay ? null : toApiTime(end),
        kind,
        note: note.trim() || null,
      }),
    onSuccess: async () => {
      onSubmitted();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:myAvailability.submitted") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("attendance:genericError"))),
  });

  const busy = mutation.isPending;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!date) return setError(t("attendance:myAvailability.dateRequired"));
    if (date < today) return setError(t("attendance:myAvailability.datePast"));
    if (!allDay) {
      if (!start || !end) return setError(t("attendance:myAvailability.timeRequired"));
      if (start >= end) return setError(t("attendance:myAvailability.endBeforeStart"));
    }
    if (note.length > NOTE_MAX_LENGTH) return setError(t("attendance:myAvailability.noteTooLong"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} size="28rem" title={t("attendance:myAvailability.dialogTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <div>
            <Text size="sm" fw={500} mb={4}>
              {t("attendance:myAvailability.dateLabel")}
            </Text>
            <DatePicker id="availability-date" value={date} min={today} disabled={busy} onChange={setDate} />
          </div>
          <div>
            <Text size="sm" fw={500} mb={4}>
              {t("attendance:myAvailability.kindLabel")}
            </Text>
            <SegmentedControl
              fullWidth
              value={kind}
              disabled={busy}
              onChange={(value) => setKind(value as AvailabilityKind)}
              data={[
                { value: "unavailable", label: t("attendance:myAvailability.kind.unavailable") },
                { value: "weeklyRestRequest", label: t("attendance:myAvailability.kind.weeklyRestRequest") },
              ]}
            />
          </div>
          <Switch
            label={t("attendance:myAvailability.allDay")}
            checked={allDay}
            disabled={busy}
            onChange={(e) => setAllDay(e.currentTarget.checked)}
          />
          {!allDay && (
            <Group grow>
              <div>
                <Text size="sm" fw={500} mb={4}>
                  {t("attendance:myAvailability.startLabel")}
                </Text>
                <TimePicker id="availability-start" value={start} disabled={busy} onChange={setStart} />
              </div>
              <div>
                <Text size="sm" fw={500} mb={4}>
                  {t("attendance:myAvailability.endLabel")}
                </Text>
                <TimePicker id="availability-end" value={end} disabled={busy} onChange={setEnd} />
              </div>
            </Group>
          )}
          <Textarea
            label={t("attendance:myAvailability.noteLabel")}
            value={note}
            disabled={busy}
            maxLength={NOTE_MAX_LENGTH}
            autosize
            minRows={2}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
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
              {t("attendance:myAvailability.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function ShiftRow({ shift, formatDate }: { shift: ShiftAssignment; formatDate: (value: string) => string }) {
  const { t } = useTranslation(["attendance"]);

  return (
    <div className="flex flex-col gap-2 rounded-md border border-[var(--mantine-color-default-border)] p-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <Group gap={6} wrap="wrap">
          <Text fw={500} tt="capitalize">
            {formatDate(shift.date)}
          </Text>
          {shift.isNight && (
            <Badge variant="light" color="indigo" leftSection={<Moon className="h-3 w-3" />}>
              {t("attendance:shift.night")}
            </Badge>
          )}
          <Badge variant={shift.status === "published" ? "filled" : "outline"} color={shift.status === "published" ? "green" : "gray"}>
            {t(`attendance:shift.status.${shift.status}`)}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          {formatTime(shift.start)} – {formatTime(shift.end)} · {t("attendance:shift.break")}: {shift.breakMinutes} dk
        </Text>
        {shift.note && (
          <Text size="sm" c="dimmed">
            {t("attendance:shift.note")}: {shift.note}
          </Text>
        )}
      </div>
    </div>
  );
}
