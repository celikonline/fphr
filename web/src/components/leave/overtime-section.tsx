import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Badge,
  Button,
  Card,
  Group,
  Input,
  Modal,
  NumberInput,
  Paper,
  Radio,
  Select,
  Skeleton,
  Stack,
  Switch,
  Text,
  Textarea,
  Title,
} from "@mantine/core";
import { ArrowRightLeft, Clock, FileSignature, Plus, Repeat, type LucideIcon } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
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
/** Column chooser trigger sized like the surrounding `size="sm"` buttons. */
const COLUMN_CHOOSER_CLASS = "h-9 px-3";

/** Today as a backend DateOnly in the user's local calendar. */
const todayDateOnly = () => toDateOnly(new Date());

/** Bordered card with an icon + title header, optional header actions and a horizontally scrollable body. */
function SectionCard({ icon: Icon, title, actions, children }: { icon: LucideIcon; title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="xs" mb="md">
        <Group gap="xs" wrap="nowrap">
          <Icon className="h-4 w-4" color="var(--mantine-color-dimmed)" />
          <Title order={3} size="h4">
            {title}
          </Title>
        </Group>
        {actions && (
          <Group gap="xs" justify="flex-end">
            {actions}
          </Group>
        )}
      </Group>
      <div className="overflow-x-auto">{children}</div>
    </Card>
  );
}

function ErrorText({ children }: { children: ReactNode }) {
  return (
    <Text size="sm" c="red">
      {children}
    </Text>
  );
}

function DialogFooter({ children }: { children: ReactNode }) {
  return <Group justify="flex-end">{children}</Group>;
}

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
    return (
      <Text size="sm" c="dimmed">
        {t("leave:overtime.noAccess")}
      </Text>
    );
  }

  return (
    <Stack gap="lg">
      <SectionCard
        icon={Clock}
        title={t("leave:overtime.cardTitle")}
        actions={
          canCreate && (
            <Button type="button" size="sm" variant="default" leftSection={<Plus className="h-4 w-4" />} onClick={() => setAdding(true)}>
              {t("leave:overtime.addOnBehalf")}
            </Button>
          )
        }
      >
        {overtimesQuery.isLoading ? (
          <Skeleton h={128} />
        ) : overtimesQuery.isError ? (
          <ErrorText>{getApiErrorMessage(overtimesQuery.error, t("leave:genericError"))}</ErrorText>
        ) : (
          <OvertimesTable
            rows={overtimesQuery.data ?? []}
            actions={
              canConvert
                ? (row) =>
                    row.status === "approved" && !row.conversionId ? (
                      <Group justify="flex-end" wrap="nowrap">
                        <Button
                          type="button"
                          size="sm"
                          variant="default"
                          leftSection={<ArrowRightLeft className="h-4 w-4" />}
                          onClick={() => setConverting(row)}
                        >
                          {t("leave:overtime.convert")}
                        </Button>
                      </Group>
                    ) : null
                : undefined
            }
          />
        )}
      </SectionCard>

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
    </Stack>
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
  // Newest first until the user sorts by a column.
  const sorted = useMemo(() => [...rows].sort((a, b) => b.convertedAt.localeCompare(a.convertedAt)), [rows]);
  const typeLabel = (row: OvertimeConversion) => (row.leaveTypeId && typeName(row.leaveTypeId)) || "";

  const columns: ColumnDef<OvertimeConversion>[] = [
    {
      id: "convertedAt",
      accessorKey: "convertedAt",
      header: t("leave:overtime.convertedAt"),
      meta: { label: t("leave:overtime.convertedAt"), cellClassName: "whitespace-nowrap text-xs text-muted-foreground" },
      cell: ({ row }) => formatDateTime(row.original.convertedAt, i18n.language),
    },
    {
      id: "workDate",
      accessorFn: (row) => {
        const overtime = overtimeById.get(row.overtimeRequestId);
        return overtime ? `${overtime.workDate} ${overtime.startTime}` : "";
      },
      header: t("leave:overtime.workDate"),
      meta: { label: t("leave:overtime.workDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => {
        const overtime = overtimeById.get(row.original.overtimeRequestId);
        return overtime ? `${formatDateOnly(overtime.workDate, i18n.language)} ${formatTimeOnly(overtime.startTime)}` : "—";
      },
    },
    {
      id: "basis",
      accessorFn: (row) => t(`leave:overtime.basisOptions.${row.minutesBasis}`),
      header: t("leave:overtime.basis"),
      meta: { label: t("leave:overtime.basis") },
    },
    {
      id: "rate",
      accessorKey: "rate",
      header: t("leave:overtime.rate"),
      meta: { label: t("leave:overtime.rate"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => `×${days(row.original.rate)}`,
    },
    {
      id: "basisMinutes",
      accessorKey: "basisMinutes",
      header: t("leave:overtime.basisMinutes"),
      meta: { label: t("leave:overtime.basisMinutes"), headerClassName: "text-right", cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => minutes(row.original.basisMinutes),
    },
    {
      id: "resultMinutes",
      accessorKey: "resultMinutes",
      header: t("leave:overtime.resultMinutes"),
      meta: { label: t("leave:overtime.resultMinutes"), headerClassName: "text-right", cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => minutes(row.original.resultMinutes),
    },
    {
      id: "resultDays",
      accessorKey: "resultDays",
      header: t("leave:overtime.resultDays"),
      meta: { label: t("leave:overtime.resultDays"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => days(row.original.resultDays),
    },
    {
      id: "leaveType",
      accessorFn: typeLabel,
      header: t("leave:ledger.type"),
      meta: { label: t("leave:ledger.type") },
      cell: ({ row }) => typeLabel(row.original) || "—",
    },
    {
      id: "status",
      accessorFn: (row) => t(`leave:overtime.conversionStatus.${row.status}`),
      header: t("leave:requests.status"),
      meta: { label: t("leave:requests.status") },
      cell: ({ row }) => (
        <Badge variant={row.original.status === "posted" ? "filled" : "outline"} color={row.original.status === "posted" ? "green" : "gray"}>
          {t(`leave:overtime.conversionStatus.${row.original.status}`)}
        </Badge>
      ),
    },
  ];

  const table = useDataTable({
    columns,
    data: sorted,
    getRowId: (row) => row.id,
    columnVisibilityKey: "senseik.leave.overtimeConversions.columns",
  });

  return (
    <SectionCard
      icon={Repeat}
      title={t("leave:overtime.conversionsTitle")}
      actions={!isLoading && !error && sorted.length > 0 && <DataTableViewOptions table={table} className={COLUMN_CHOOSER_CLASS} />}
    >
      {isLoading ? (
        <Skeleton h={96} />
      ) : error ? (
        <ErrorText>{error}</ErrorText>
      ) : sorted.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:overtime.conversionsEmpty")}
        </Text>
      ) : (
        <DataTable table={table} />
      )}
    </SectionCard>
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
    <Modal opened onClose={onClose} title={t("leave:overtime.convertTitle")} size="lg">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:overtime.convertDescription", {
              date: formatDateOnly(overtime.workDate, i18n.language),
              time: formatTimeOnly(overtime.startTime),
              duration: minutes(overtime.durationMinutes),
              payable: minutes(overtime.payableMinutes),
            })}
          </Text>
          <Radio.Group label={t("leave:overtime.basis")} value={basis} onChange={(value) => setBasis(value as OvertimeMinutesBasis)} disabled={busy}>
            <Stack gap="xs" mt="xs">
              <Radio id="basis-payable" value="payable" label={`${t("leave:overtime.basisOptions.payable")} (${minutes(overtime.payableMinutes)})`} />
              {allowFull && (
                <Radio id="basis-full" value="full" label={`${t("leave:overtime.basisOptions.full")} (${minutes(overtime.durationMinutes)})`} />
              )}
            </Stack>
          </Radio.Group>
          <div className="grid gap-4 sm:grid-cols-2">
            <NumberInput
              id="convert-rate"
              label={t("leave:overtime.rate")}
              withAsterisk
              inputMode="decimal"
              min={MIN_RATE}
              max={MAX_RATE}
              step={0.05}
              allowNegative={false}
              value={rate}
              disabled={busy}
              onChange={(value) => setRate(String(value))}
            />
            {typeOptions.length > 0 && (
              <Select
                id="convert-type"
                label={t("leave:ledger.type")}
                data={[
                  { value: DEFAULT_TYPE, label: t("leave:overtime.defaultType") },
                  ...typeOptions.map((option) => ({ value: option.id, label: option.name })),
                ]}
                value={leaveTypeId}
                allowDeselect={false}
                disabled={busy}
                onChange={(value) => value && setLeaveTypeId(value)}
              />
            )}
          </div>
          {rateValid && (
            <Paper withBorder p="sm" bg="var(--mantine-color-default-hover)" aria-live="polite">
              <Text size="sm">{t("leave:overtime.convertPreview", { minutes: minutes(Math.round(basisMinutes * parsedRate)) })}</Text>
            </Paper>
          )}
          <Textarea
            id="convert-note"
            label={t("leave:overtime.note")}
            value={note}
            maxLength={NOTE_MAX_LENGTH}
            disabled={busy}
            autosize
            minRows={3}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
          {error && <ErrorText>{error}</ErrorText>}
          <DialogFooter>
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("leave:overtime.convert")}
            </Button>
          </DialogFooter>
        </Stack>
      </form>
    </Modal>
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
  // Newest first until the user sorts by a column.
  const sorted = useMemo(() => [...(query.data ?? [])].sort((a, b) => b.givenAt.localeCompare(a.givenAt)), [query.data]);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["leave", "overtimes", employeeId, "consents"] });

  const columns: ColumnDef<OvertimeConsent>[] = [
    {
      id: "givenAt",
      accessorKey: "givenAt",
      enableHiding: false,
      header: t("leave:overtime.givenAt"),
      meta: { label: t("leave:overtime.givenAt"), cellClassName: "whitespace-nowrap font-medium" },
      cell: ({ row }) => date(row.original.givenAt),
    },
    {
      id: "source",
      accessorFn: (consent) => t(`leave:overtime.consentSources.${consent.source}`),
      header: t("leave:overtime.consentSource"),
      meta: { label: t("leave:overtime.consentSource") },
    },
    {
      id: "coversNightWork",
      accessorFn: (consent) => (consent.coversNightWork ? t("common:yes") : t("common:no")),
      header: t("leave:overtime.coversNightWork"),
      meta: { label: t("leave:overtime.coversNightWork") },
    },
    {
      id: "revocationNoticeAt",
      accessorFn: (consent) => consent.revocationNoticeAt ?? "",
      header: t("leave:overtime.revocationNoticeAt"),
      meta: { label: t("leave:overtime.revocationNoticeAt"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => date(row.original.revocationNoticeAt),
    },
    {
      id: "revocationEffectiveAt",
      accessorFn: (consent) => consent.revocationEffectiveAt ?? "",
      header: t("leave:overtime.revocationEffectiveAt"),
      meta: { label: t("leave:overtime.revocationEffectiveAt"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => date(row.original.revocationEffectiveAt),
    },
    {
      id: "status",
      accessorFn: (consent) => t(`leave:overtime.consentStates.${consentState(consent, today)}`),
      header: t("leave:requests.status"),
      meta: { label: t("leave:requests.status") },
      cell: ({ row }) => {
        const state = consentState(row.original, today);
        return (
          <Badge
            variant={state === "inEffect" ? "filled" : state === "revoked" ? "outline" : "light"}
            color={state === "inEffect" ? "green" : state === "revoked" ? "gray" : undefined}
          >
            {t(`leave:overtime.consentStates.${state}`)}
          </Badge>
        );
      },
    },
    {
      id: "actions",
      enableSorting: false,
      enableHiding: false,
      header: () => null,
      meta: { headerClassName: "w-0" },
      cell: ({ row }) =>
        !row.original.revocationNoticeAt && (
          <div onClick={(event) => event.stopPropagation()}>
            <Button type="button" size="sm" variant="subtle" onClick={() => setRevoking(row.original)}>
              {t("leave:overtime.revokeConsent")}
            </Button>
          </div>
        ),
    },
  ];

  const table = useDataTable({
    columns,
    data: sorted,
    getRowId: (consent) => consent.id,
    columnVisibilityKey: "senseik.leave.overtimeConsents.columns",
  });

  return (
    <SectionCard
      icon={FileSignature}
      title={t("leave:overtime.consentsTitle")}
      actions={
        <>
          {!query.isLoading && !query.isError && sorted.length > 0 && <DataTableViewOptions table={table} className={COLUMN_CHOOSER_CLASS} />}
          <Button type="button" size="sm" variant="default" leftSection={<Plus className="h-4 w-4" />} onClick={() => setRecording(true)}>
            {t("leave:overtime.recordConsent")}
          </Button>
        </>
      }
    >
      {query.isLoading ? (
        <Skeleton h={96} />
      ) : query.isError ? (
        <ErrorText>{getApiErrorMessage(query.error, t("leave:genericError"))}</ErrorText>
      ) : sorted.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:overtime.consentsEmpty")}
        </Text>
      ) : (
        <DataTable table={table} />
      )}

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
    </SectionCard>
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
    <Modal opened onClose={onClose} title={t("leave:overtime.recordConsent")} size="md">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:overtime.recordConsentDescription")}
          </Text>
          <div className="grid gap-4 sm:grid-cols-2">
            <Input.Wrapper id="consent-date" label={t("leave:overtime.givenAt")} withAsterisk>
              <DatePicker id="consent-date" value={givenAt} disabled={busy} onChange={setGivenAt} />
            </Input.Wrapper>
            <Select
              id="consent-source"
              label={t("leave:overtime.consentSource")}
              data={[
                { value: "employmentContract", label: t("leave:overtime.consentSources.employmentContract") },
                { value: "standaloneForm", label: t("leave:overtime.consentSources.standaloneForm") },
              ]}
              value={source}
              allowDeselect={false}
              disabled={busy}
              onChange={(value) => value && setSource(value as OvertimeConsentSource)}
            />
          </div>
          <Paper withBorder p="sm">
            <Switch
              id="consent-night"
              label={t("leave:overtime.coversNightWork")}
              description={t("leave:overtime.coversNightWorkHint")}
              labelPosition="left"
              classNames={{ body: "justify-between gap-4" }}
              checked={coversNightWork}
              disabled={busy}
              onChange={(event) => setCoversNightWork(event.currentTarget.checked)}
            />
          </Paper>
          {error && <ErrorText>{error}</ErrorText>}
          <DialogFooter>
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </Stack>
      </form>
    </Modal>
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
    <Modal opened onClose={onClose} title={t("leave:overtime.revokeTitle")} size="md">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:overtime.revokeDescription", { date: formatDateOnly(consent.givenAt, i18n.language) })}
          </Text>
          <Input.Wrapper id="revoke-notice" label={t("leave:overtime.revocationNoticeAt")} withAsterisk>
            <DatePicker id="revoke-notice" value={noticeAt} min={consent.givenAt} disabled={busy} onChange={setNoticeAt} />
          </Input.Wrapper>
          {error && <ErrorText>{error}</ErrorText>}
          <DialogFooter>
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" color="red" disabled={busy}>
              {busy ? t("common:saving") : t("leave:overtime.revokeConsent")}
            </Button>
          </DialogFooter>
        </Stack>
      </form>
    </Modal>
  );
}
