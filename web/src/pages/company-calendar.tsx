import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Badge, Button, Group, Modal, Paper, Radio, Skeleton, Stack, Text, Textarea, TextInput, Title, Tooltip } from "@mantine/core";
import { CalendarDays, ChevronLeft, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import {
  createCompanyEvent,
  deleteCompanyEvent,
  getCompanyCalendar,
  updateCompanyEvent,
  type CompanyCalendarEntry,
  type CompanyEventType,
} from "@/services/company-calendar.service";
import classes from "./company-calendar.module.css";

const CALENDAR_QUERY_KEY = "companyCalendar";
const MANUAL_TYPES: Extract<CompanyEventType, "companyEvent" | "meeting">[] = ["companyEvent", "meeting"];

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** Monday-first grid covering the whole month, padded with the tail of the previous/next month. */
function useMonthGrid(month: Date): Date[] {
  return useMemo(() => {
    const first = startOfMonth(month);
    const last = new Date(month.getFullYear(), month.getMonth() + 1, 0);
    const leadingDays = (first.getDay() + 6) % 7; // Monday = 0
    const trailingDays = (7 - (((last.getDay() + 6) % 7) + 1)) % 7;
    const start = new Date(first);
    start.setDate(first.getDate() - leadingDays);
    const end = new Date(last);
    end.setDate(last.getDate() + trailingDays);

    const days: Date[] = [];
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      days.push(new Date(d));
    }
    return days;
  }, [month]);
}

/** Şirket Etkinlik Takvimi (senseik-027): month grid merging manual events with derived birthdays/leave starts. */
export default function CompanyCalendarPage() {
  const canView = usePermission("announcements.my.events");
  if (!canView) return <NoAccess />;
  return <CompanyCalendar />;
}

function CompanyCalendar() {
  const { t, i18n } = useTranslation(["companyCalendar", "common", "employees"]);
  const canManage = usePermission("announcements.events.manage");
  const { nameById } = useOrgUnits();
  const [month, setMonth] = useState(() => startOfMonth(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editingEvent, setEditingEvent] = useState<CompanyCalendarEntry | null>(null);
  const [pendingDelete, setPendingDelete] = useState<CompanyCalendarEntry | null>(null);

  const days = useMonthGrid(month);
  const from = toDateOnly(days[0]);
  const to = toDateOnly(days[days.length - 1]);
  const today = toDateOnly(new Date());

  const query = useQuery({
    queryKey: [CALENDAR_QUERY_KEY, from, to, orgUnitId],
    queryFn: () => getCompanyCalendar({ from, to, orgUnitId }),
    retry: false,
  });

  const entriesByDate = useMemo(() => {
    const map = new Map<string, CompanyCalendarEntry[]>();
    for (const entry of query.data ?? []) {
      const list = map.get(entry.date) ?? [];
      list.push(entry);
      map.set(entry.date, list);
    }
    return map;
  }, [query.data]);

  const monthLabel = new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(month);
  // Monday-first header, matching useMonthGrid's layout.
  const weekdayLabels = useMemo(() => {
    const weekday = new Intl.DateTimeFormat(i18n.language, { weekday: "short" });
    const monday = new Date(2026, 0, 5); // an arbitrary known Monday
    return Array.from({ length: 7 }, (_, i) => {
      const d = new Date(monday);
      d.setDate(monday.getDate() + i);
      return weekday.format(d);
    });
  }, [i18n.language]);
  const isWeekend = (date: Date) => date.getDay() === 0 || date.getDay() === 6;
  const shift = (delta: number) => setMonth((current) => new Date(current.getFullYear(), current.getMonth() + delta, 1));

  const typeLabel = (type: CompanyEventType) => t(`companyCalendar:type.${type}`);

  return (
    <div className={`${classes.root} flex max-w-[80rem] flex-col gap-6 p-6 md:p-10`}>
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarDays className="h-6 w-6" /> {t("companyCalendar:title")}
          </Title>
          <Text c="dimmed">{t("companyCalendar:subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setCreateOpen(true)}>
            {t("companyCalendar:create")}
          </Button>
        )}
      </Group>

      <Group justify="space-between" gap="sm">
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("companyCalendar:previous")}>
            <ActionIcon type="button" variant="default" size="lg" aria-label={t("companyCalendar:previous")} onClick={() => shift(-1)}>
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Button type="button" variant="default" size="sm" onClick={() => setMonth(startOfMonth(new Date()))}>
            {t("companyCalendar:today")}
          </Button>
          <Tooltip label={t("companyCalendar:next")}>
            <ActionIcon type="button" variant="default" size="lg" aria-label={t("companyCalendar:next")} onClick={() => shift(1)}>
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Title order={2} size="h4" ml="xs" tt="capitalize">
            {monthLabel}
          </Title>
        </Group>
        <Group gap="md">
          <Group gap="sm" wrap="nowrap">
            {(["companyEvent", "meeting", "birthday", "leaveStart"] as const).map((type) => (
              <Group key={type} gap={6} wrap="nowrap">
                <span className={`${classes.legendDot} inline-block h-2.5 w-2.5`} data-type={type} />
                <Text size="xs" c="dimmed">
                  {typeLabel(type)}
                </Text>
              </Group>
            ))}
          </Group>
          {nameById.size > 0 && (
            <div className="w-64">
              <label htmlFor="company-calendar-unit" className="sr-only">
                {t("employees:fields.orgUnit")}
              </label>
              <OrgUnitSelect id="company-calendar-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
            </div>
          )}
        </Group>
      </Group>

      <Paper withBorder className="overflow-hidden">
        {query.isLoading ? (
          <div className="p-4">
            <Skeleton h={480} />
          </div>
        ) : query.isError ? (
          <Text size="sm" c="red" p="md">
            {getApiErrorMessage(query.error, t("companyCalendar:genericError"))}
          </Text>
        ) : (
          <div className={`${classes.grid} grid grid-cols-7`}>
            {weekdayLabels.map((label) => (
              <div key={label} className={`${classes.weekdayHeader} px-2 py-2 text-xs`}>
                {label}
              </div>
            ))}
            {days.map((date) => {
              const key = toDateOnly(date);
              const entries = entriesByDate.get(key) ?? [];
              const visible = entries.slice(0, 3);
              const overflow = entries.length - visible.length;
              return (
                <div
                  key={key}
                  className={`${classes.dayCell} flex flex-col gap-1 p-1.5`}
                  data-weekend={isWeekend(date) || undefined}
                  data-today={key === today || undefined}
                  data-outside={date.getMonth() !== month.getMonth() || undefined}
                >
                  <span className={`${classes.dayNumber} text-xs`}>{date.getDate()}</span>
                  <div className="flex flex-col gap-1">
                    {visible.map((entry, index) => (
                      <EventChip
                        key={entry.id ?? `${entry.type}-${entry.employeeId}-${index}`}
                        entry={entry}
                        label={typeLabel(entry.type)}
                        canManage={canManage}
                        onEdit={setEditingEvent}
                        onDelete={setPendingDelete}
                      />
                    ))}
                    {overflow > 0 && (
                      <Text size="xs" c="dimmed">
                        {t("companyCalendar:moreCount", { count: overflow })}
                      </Text>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </Paper>

      {createOpen && <EventFormDialog onClose={() => setCreateOpen(false)} />}
      {editingEvent && <EventFormDialog editing={editingEvent} onClose={() => setEditingEvent(null)} />}
      {pendingDelete && <DeleteEventDialog entry={pendingDelete} onClose={() => setPendingDelete(null)} />}
    </div>
  );
}

function EventChip({
  entry,
  label,
  canManage,
  onEdit,
  onDelete,
}: {
  entry: CompanyCalendarEntry;
  label: string;
  canManage: boolean;
  onEdit: (entry: CompanyCalendarEntry) => void;
  onDelete: (entry: CompanyCalendarEntry) => void;
}) {
  // Only manual (companyEvent/meeting) entries carry an id — derived birthday/leave-start entries have none.
  const canModify = canManage && Boolean(entry.id);
  return (
    <Tooltip label={`${label}: ${entry.title}`} multiline maw={240}>
      <Badge
        className={classes.chip}
        data-type={entry.type}
        variant="light"
        radius="sm"
        styles={{ root: { display: "block", width: "100%" }, label: { overflow: "hidden", textOverflow: "ellipsis" } }}
        rightSection={
          canModify ? (
            <Group gap={2} wrap="nowrap">
              <ActionIcon
                size="xs"
                variant="transparent"
                aria-label="edit"
                onClick={(event) => {
                  event.stopPropagation();
                  onEdit(entry);
                }}
              >
                <Pencil size={11} />
              </ActionIcon>
              <ActionIcon
                size="xs"
                variant="transparent"
                aria-label="delete"
                onClick={(event) => {
                  event.stopPropagation();
                  onDelete(entry);
                }}
              >
                <Trash2 size={11} />
              </ActionIcon>
            </Group>
          ) : undefined
        }
      >
        {entry.title}
      </Badge>
    </Tooltip>
  );
}

function eventErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "company_event.title_required") return t("companyCalendar:errors.titleRequired");
  return getApiErrorMessage(error, t("companyCalendar:errors.generic"));
}

function EventFormDialog({ editing, onClose }: { editing?: CompanyCalendarEntry; onClose: () => void }) {
  const { t } = useTranslation(["companyCalendar", "common", "employees"]);
  const queryClient = useQueryClient();

  const initialType: (typeof MANUAL_TYPES)[number] =
    editing && (MANUAL_TYPES as CompanyEventType[]).includes(editing.type) ? (editing.type as (typeof MANUAL_TYPES)[number]) : "companyEvent";

  const [title, setTitle] = useState(editing?.title ?? "");
  const [description, setDescription] = useState(editing?.description ?? "");
  const [eventDate, setEventDate] = useState(editing?.date ?? toDateOnly(new Date()));
  const [type, setType] = useState<(typeof MANUAL_TYPES)[number]>(initialType);
  const [visibility, setVisibility] = useState<"all" | "orgUnit">(editing?.visibilityOrgUnitId ? "orgUnit" : "all");
  const [visibilityOrgUnitId, setVisibilityOrgUnitId] = useState<string | null>(editing?.visibilityOrgUnitId ?? null);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      const payload = {
        title: title.trim(),
        description: description.trim() || null,
        eventDate,
        type,
        visibilityOrgUnitId: visibility === "orgUnit" ? visibilityOrgUnitId : null,
      };
      if (editing) {
        await updateCompanyEvent(editing.id as string, payload);
      } else {
        await createCompanyEvent(payload);
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CALENDAR_QUERY_KEY] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(editing ? "companyCalendar:updated" : "companyCalendar:saved"),
      });
      onClose();
    },
    onError: (err) => setError(eventErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!title.trim()) return setError(t("companyCalendar:errors.titleRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t(editing ? "companyCalendar:edit" : "companyCalendar:create")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="company-event-title"
            label={t("companyCalendar:form.title")}
            placeholder={t("companyCalendar:form.titlePlaceholder")}
            withAsterisk
            value={title}
            maxLength={200}
            onChange={(event) => setTitle(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <Textarea
            id="company-event-description"
            label={t("companyCalendar:form.description")}
            placeholder={t("companyCalendar:form.descriptionPlaceholder")}
            value={description}
            maxLength={2000}
            autosize
            minRows={2}
            onChange={(event) => setDescription(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <DatePicker id="company-event-date" value={eventDate} onChange={setEventDate} disabled={mutation.isPending} />
          <Radio.Group
            label={t("companyCalendar:form.type")}
            value={type}
            onChange={(value) => setType(value as (typeof MANUAL_TYPES)[number])}
          >
            <Group gap="md" mt={4}>
              {MANUAL_TYPES.map((value) => (
                <Radio key={value} value={value} label={t(`companyCalendar:type.${value}`)} disabled={mutation.isPending} />
              ))}
            </Group>
          </Radio.Group>
          <Radio.Group
            label={t("companyCalendar:form.visibility")}
            value={visibility}
            onChange={(value) => setVisibility(value as "all" | "orgUnit")}
          >
            <Group gap="md" mt={4}>
              <Radio value="all" label={t("companyCalendar:form.visibilityAll")} disabled={mutation.isPending} />
              <Radio value="orgUnit" label={t("companyCalendar:form.visibilityOrgUnit")} disabled={mutation.isPending} />
            </Group>
          </Radio.Group>
          {visibility === "orgUnit" && (
            <OrgUnitSelect
              id="company-event-org-unit"
              value={visibilityOrgUnitId}
              onChange={setVisibilityOrgUnitId}
              emptyLabel={t("employees:filters.allUnits")}
              disabled={mutation.isPending}
            />
          )}
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function DeleteEventDialog({ entry, onClose }: { entry: CompanyCalendarEntry; onClose: () => void }) {
  const { t, i18n } = useTranslation(["companyCalendar", "common"]);
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => deleteCompanyEvent(entry.id as string),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CALENDAR_QUERY_KEY] });
      toast({ variant: "success", title: t("common:success"), description: t("companyCalendar:deleted") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("companyCalendar:errors.generic"))),
  });

  return (
    <Modal opened onClose={onClose} title={t("companyCalendar:deleteConfirmTitle")} size="28rem">
      <Stack gap="md">
        <Text size="sm">
          {t("companyCalendar:deleteConfirmMessage", { title: entry.title })} ({formatDateOnly(entry.date, i18n.language)})
        </Text>
        {error && (
          <Text size="sm" c="red">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" color="red" loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t("companyCalendar:delete")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
