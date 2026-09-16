import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ActionIcon, Alert, Button, Card, Group, Input, Modal, NumberInput, Select, Skeleton, Stack, Text, Textarea, Title, Tooltip } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { AlertTriangle, RotateCcw, UserX } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly, formatDateTime, toDateOnly } from "@/lib/dates";
import { addAbsence, listAbsences, reverseAbsence, type Absence, type AbsenceInput, type AbsenceKind } from "@/services/leave.service";

/** Narrow phones (S-6 mobil kalıbı, `missing-documents-report.tsx`): a card list reads better than a horizontally-scrolled table. */
const MOBILE_QUERY = "(max-width: 390px)";

const ABSENCES_COLUMNS_KEY = "senseik.leave.absences.columns";
const NOTE_MAX_LENGTH = 500;
const ABSENCE_KINDS: AbsenceKind[] = ["unexcused", "excused", "late"];

/** Current calendar year (Devamsızlık is a per-year employee card view, like İzin Özeti's current period). */
function currentYearRange(): { from: string; to: string } {
  const year = new Date().getFullYear();
  return { from: `${year}-01-01`, to: `${year}-12-31` };
}

/**
 * G-5-10 client-side heads-up (4857 md.25/II-g): ≥3 `unexcused` days in the same calendar month, or 2 consecutive
 * calendar days, counting only active original records (not reversed, not itself a reversal — same predicate as the
 * "Ters kayıt" column action). No server call; this is not a legal determination, only an information banner.
 */
function exceedsUnexcusedThreshold(items: Absence[]): boolean {
  const active = items.filter((item) => item.kind === "unexcused" && !item.isReversed && !item.reversesId);
  const byMonth = new Map<string, number>();
  for (const item of active) {
    const month = item.date.slice(0, 7);
    byMonth.set(month, (byMonth.get(month) ?? 0) + 1);
  }
  if ([...byMonth.values()].some((count) => count >= 3)) return true;
  const dates = [...new Set(active.map((item) => item.date))].sort();
  const asUtcMs = (value: string) => {
    const [year, month, day] = value.split("-").map(Number);
    return Date.UTC(year, month - 1, day);
  };
  return dates.some((date, index) => index > 0 && asUtcMs(date) - asUtcMs(dates[index - 1]) === 24 * 60 * 60 * 1000);
}

function Strike({ reversed, children }: { reversed: boolean; children: ReactNode }) {
  return <span style={reversed ? { textDecoration: "line-through", opacity: 0.6 } : undefined}>{children}</span>;
}

interface AbsencesPanelProps {
  employeeId: string;
}

/** Employee card → Devamsızlık sekmesi: İK'nın elle girdiği mazeretsiz/mazeretli/geç kalma kayıtları (append-only, S-7). */
export function AbsencesPanel({ employeeId }: AbsencesPanelProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const canView = usePermission("leave.absences.view");
  const canManage = usePermission("leave.absences.manage");
  const [addOpen, setAddOpen] = useState(false);
  const [reversing, setReversing] = useState<Absence | null>(null);
  const queryClient = useQueryClient();
  const range = useMemo(() => currentYearRange(), []);

  const query = useQuery({
    queryKey: ["leave", "absences", employeeId, range.from, range.to],
    queryFn: () => listAbsences(employeeId, range.from, range.to),
    enabled: canView,
    retry: false,
  });

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ["leave", "absences", employeeId] });
  const rows = useMemo(() => [...(query.data ?? [])].sort((a, b) => b.date.localeCompare(a.date)), [query.data]);
  const exceedsThreshold = useMemo(() => exceedsUnexcusedThreshold(rows), [rows]);
  const showThresholdAlert = canManage && exceedsThreshold;
  const isMobile = useMediaQuery(MOBILE_QUERY);

  const columns: ColumnDef<Absence>[] = [
    {
      id: "date",
      accessorKey: "date",
      header: t("leave:absences.date"),
      meta: { label: t("leave:absences.date"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => <Strike reversed={row.original.isReversed}>{formatDateOnly(row.original.date, i18n.language)}</Strike>,
    },
    {
      id: "kind",
      accessorFn: (row) => t(`leave:absences.kind.${row.kind}`),
      header: t("leave:absences.kindLabel"),
      meta: { label: t("leave:absences.kindLabel") },
      cell: ({ row }) => <Strike reversed={row.original.isReversed}>{t(`leave:absences.kind.${row.original.kind}`)}</Strike>,
    },
    {
      id: "hours",
      accessorFn: (row) => row.hours ?? null,
      header: t("leave:absences.hours"),
      meta: { label: t("leave:absences.hours"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => <Strike reversed={row.original.isReversed}>{row.original.hours ?? "—"}</Strike>,
    },
    {
      id: "note",
      accessorFn: (row) => row.note ?? "",
      enableSorting: false,
      header: t("leave:absences.note"),
      meta: { label: t("leave:absences.note"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) =>
        row.original.note ? (
          <Strike reversed={row.original.isReversed}>
            <span title={row.original.note}>{row.original.note}</span>
          </Strike>
        ) : null,
    },
    {
      id: "recordedBy",
      accessorKey: "recordedBy",
      enableSorting: false,
      header: t("leave:absences.recordedBy"),
      meta: { label: t("leave:absences.recordedBy") },
      cell: ({ row }) => (
        <Text span size="xs" c="dimmed">
          {row.original.recordedBy} · {formatDateTime(row.original.recordedAt, i18n.language)}
        </Text>
      ),
    },
    {
      id: "status",
      enableSorting: false,
      header: t("leave:absences.status"),
      meta: { label: t("leave:absences.status") },
      cell: ({ row }) =>
        row.original.isReversed ? (
          <Text span size="xs" c="dimmed">
            {t("leave:absences.reversed")}
          </Text>
        ) : row.original.reversesId ? (
          <Text span size="xs" c="dimmed">
            {t("leave:absences.reversal")}
          </Text>
        ) : null,
    },
    ...(canManage
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => null,
            meta: { headerClassName: "w-0" },
            cell: ({ row }) =>
              !row.original.isReversed && !row.original.reversesId ? (
                <Tooltip label={t("leave:absences.reverse")}>
                  <ActionIcon
                    type="button"
                    variant="subtle"
                    color="gray"
                    aria-label={t("leave:absences.reverse")}
                    onClick={() => setReversing(row.original)}
                  >
                    <RotateCcw size={16} />
                  </ActionIcon>
                </Tooltip>
              ) : null,
          } satisfies ColumnDef<Absence>,
        ]
      : []),
  ];

  const table = useDataTable({ columns, data: rows, getRowId: (row) => row.id, columnVisibilityKey: ABSENCES_COLUMNS_KEY, pageSize: 20 });

  if (!canView) {
    return (
      <Text size="sm" c="dimmed">
        {t("leave:absences.noAccess")}
      </Text>
    );
  }

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="xs" mb="md">
        <Group gap="xs">
          <UserX size={16} color="var(--mantine-color-dimmed)" />
          <Title order={4}>{t("leave:absences.title")}</Title>
        </Group>
        {canManage && (
          <Button type="button" size="sm" onClick={() => setAddOpen(true)}>
            {t("leave:absences.add")}
          </Button>
        )}
      </Group>
      {showThresholdAlert && (
        <Alert color="yellow" variant="light" icon={<AlertTriangle size={16} />} mb="md">
          {t("leave:absences.thresholdAlert")}
        </Alert>
      )}
      {query.isLoading ? (
        <Skeleton h={128} />
      ) : query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("leave:genericError"))}
        </Text>
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:absences.empty")}
        </Text>
      ) : isMobile ? (
        <AbsencesMobileList items={rows} canManage={canManage} onReverse={setReversing} />
      ) : (
        <Stack gap="xs">
          <Group justify="flex-end">
            <DataTableViewOptions table={table} className="h-9 px-3" />
          </Group>
          <DataTable table={table} />
        </Stack>
      )}

      {addOpen && (
        <AddAbsenceDialog
          employeeId={employeeId}
          onClose={() => setAddOpen(false)}
          onSaved={() => {
            setAddOpen(false);
            toast({ variant: "success", title: t("common:success"), description: t("leave:absences.added") });
            invalidate();
          }}
        />
      )}

      {reversing && (
        <ReverseAbsenceDialog
          employeeId={employeeId}
          absence={reversing}
          onClose={() => setReversing(null)}
          onReversed={() => {
            setReversing(null);
            toast({ variant: "success", title: t("common:success"), description: t("leave:absences.reversedDone") });
            invalidate();
          }}
        />
      )}
    </Card>
  );
}

/** ≤390px card list mirroring the desktop table's columns (S-6 mobil kalıbı). */
function AbsencesMobileList({
  items,
  canManage,
  onReverse,
}: {
  items: Absence[];
  canManage: boolean;
  onReverse: (absence: Absence) => void;
}) {
  const { t, i18n } = useTranslation(["leave"]);

  return (
    <Stack gap="sm">
      {items.map((item) => (
        <Card key={item.id} withBorder padding="md">
          <Stack gap={4}>
            <Group justify="space-between" align="flex-start" wrap="nowrap">
              <Strike reversed={item.isReversed}>
                <Text size="sm" fw={500}>
                  {formatDateOnly(item.date, i18n.language)}
                </Text>
              </Strike>
              {canManage && !item.isReversed && !item.reversesId && (
                <Tooltip label={t("leave:absences.reverse")}>
                  <ActionIcon type="button" variant="subtle" color="gray" aria-label={t("leave:absences.reverse")} onClick={() => onReverse(item)}>
                    <RotateCcw size={16} />
                  </ActionIcon>
                </Tooltip>
              )}
            </Group>
            <Strike reversed={item.isReversed}>
              <Text size="sm" c="dimmed">
                {t(`leave:absences.kind.${item.kind}`)}
                {item.hours != null ? ` · ${item.hours} ${t("leave:absences.hours")}` : ""}
              </Text>
            </Strike>
            {item.note && (
              <Strike reversed={item.isReversed}>
                <Text size="sm">{item.note}</Text>
              </Strike>
            )}
            <Text size="xs" c="dimmed">
              {item.recordedBy} · {formatDateTime(item.recordedAt, i18n.language)}
            </Text>
            {item.isReversed ? (
              <Text size="xs" c="dimmed">
                {t("leave:absences.reversed")}
              </Text>
            ) : item.reversesId ? (
              <Text size="xs" c="dimmed">
                {t("leave:absences.reversal")}
              </Text>
            ) : null}
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}

interface AddAbsenceDialogProps {
  employeeId: string;
  onClose: () => void;
  onSaved: () => void;
}

/** "Devamsızlık ekle": `late` türünde saat zorunlu, diğer türlerde gizli (BE `AbsenceHoursOnlyForLate`). */
function AddAbsenceDialog({ employeeId, onClose, onSaved }: AddAbsenceDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const [date, setDate] = useState(toDateOnly(new Date()));
  const [kind, setKind] = useState<AbsenceKind>("unexcused");
  const [hours, setHours] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: AbsenceInput) => addAbsence(employeeId, input),
    onSuccess: onSaved,
    onError: (err) => {
      const problem = getApiProblem(err);
      setError(
        problem?.code === "leave.absence.conflicts_with_leave"
          ? t("leave:absences.conflictsWithLeave")
          : getApiErrorMessage(err, t("leave:genericError")),
      );
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!date) return setError(t("leave:absences.validation.dateRequired"));
    const parsedHours = hours.trim() === "" ? null : Number(hours);
    if (kind === "late" && (parsedHours === null || !Number.isFinite(parsedHours) || parsedHours <= 0 || parsedHours > 24)) {
      return setError(t("leave:absences.validation.hoursInvalid"));
    }
    setError(null);
    mutation.mutate({ date, kind, hours: kind === "late" ? parsedHours : null, note: note.trim() || null });
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} size="28rem" title={t("leave:absences.addTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Input.Wrapper id="absence-date" label={t("leave:absences.date")} withAsterisk>
            <DatePicker id="absence-date" value={date} disabled={busy} onChange={setDate} max={toDateOnly(new Date())} />
          </Input.Wrapper>
          <Select
            id="absence-kind"
            label={t("leave:absences.kindLabel")}
            withAsterisk
            allowDeselect={false}
            disabled={busy}
            value={kind}
            onChange={(value) => setKind((value as AbsenceKind | null) ?? "unexcused")}
            data={ABSENCE_KINDS.map((k) => ({ value: k, label: t(`leave:absences.kind.${k}`) }))}
          />
          {kind === "late" && (
            <NumberInput
              id="absence-hours"
              label={t("leave:absences.hours")}
              withAsterisk
              min={0.01}
              max={24}
              step={0.5}
              decimalScale={2}
              disabled={busy}
              value={hours}
              onChange={(value) => setHours(String(value))}
            />
          )}
          <Textarea
            id="absence-note"
            label={t("leave:absences.note")}
            value={note}
            maxLength={NOTE_MAX_LENGTH}
            disabled={busy}
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
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

interface ReverseAbsenceDialogProps {
  employeeId: string;
  absence: Absence;
  onClose: () => void;
  onReversed: () => void;
}

/** "Ters kayıt": append-only düzeltme, orijinal kayıt silinmez (409 leave.absence.already_reversed). */
function ReverseAbsenceDialog({ employeeId, absence, onClose, onReversed }: ReverseAbsenceDialogProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => reverseAbsence(employeeId, absence.id, value),
    onSuccess: onReversed,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return setError(t("leave:absences.validation.reasonRequired"));
    setError(null);
    mutation.mutate(reason.trim());
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} size="28rem" title={t("leave:absences.reverseTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:absences.reverseDescription", {
              kind: t(`leave:absences.kind.${absence.kind}`),
              date: formatDateOnly(absence.date, i18n.language),
            })}
          </Text>
          <Textarea
            id="absence-reverse-reason"
            label={t("leave:absences.reverseReason")}
            withAsterisk
            value={reason}
            maxLength={NOTE_MAX_LENGTH}
            disabled={busy}
            autosize
            minRows={3}
            onChange={(e) => setReason(e.currentTarget.value)}
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
            <Button type="submit" color="red" loading={busy}>
              {busy ? t("common:saving") : t("leave:absences.reverseConfirm")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
