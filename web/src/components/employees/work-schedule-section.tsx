import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Modal, Select, Skeleton, Stack, Table, Text, Title } from "@mantine/core";
import { CalendarClock, Plus } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  assignWorkSchedule,
  getEmployeeWorkScheduleAssignments,
  getMyWorkScheduleAssignments,
  listWorkSchedules,
  type EmployeeWorkScheduleAssignment,
} from "@/services/attendance.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** "6 yıl 9 ay 24 gün" (11.3.6 Çalışma Takvimi geçmişi "Süre" kolonu); `to` defaults to today for the ongoing row. */
function formatDuration(from: string, to: string | null | undefined, t: (key: string, opts: { count: number }) => string): string {
  const end = to ? new Date(to) : new Date();
  const start = new Date(from);
  let years = end.getFullYear() - start.getFullYear();
  let months = end.getMonth() - start.getMonth();
  let days = end.getDate() - start.getDate();
  if (days < 0) {
    months -= 1;
    const daysInPrevMonth = new Date(end.getFullYear(), end.getMonth(), 0).getDate();
    days += daysInPrevMonth;
  }
  if (months < 0) {
    years -= 1;
    months += 12;
  }
  const parts: string[] = [];
  if (years > 0) parts.push(t("employees:career.workSchedule.duration.years", { count: years }));
  if (months > 0) parts.push(t("employees:career.workSchedule.duration.months", { count: months }));
  if (days > 0 || parts.length === 0) parts.push(t("employees:career.workSchedule.duration.days", { count: days }));
  return parts.join(" ");
}

/** "Kariyer &gt; Çalışma Takvimi" alt sekmesi: dated work-schedule assignment history + "Ata" action. */
export function WorkScheduleSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canView = usePermission("attendance.workschedule.view");
  const canAssign = usePermission("attendance.workschedule.assign");
  const canViewMine = usePermission("attendance.my.workschedule");
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  const isOwnCard = !canView && ownEmployeeId === employee.id;
  const [assignOpen, setAssignOpen] = useState(false);

  const enabled = canView || (isOwnCard && canViewMine);
  const query = useQuery({
    queryKey: ["attendance", "work-schedule-assignments", isOwnCard ? "me" : employee.id],
    queryFn: () => (isOwnCard ? getMyWorkScheduleAssignments() : getEmployeeWorkScheduleAssignments(employee.id)),
    enabled,
  });

  const date = (value: string) => formatDateOnly(value, i18n.language);

  if (!enabled) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed">
          {t("common:noAccessDescription")}
        </Text>
      </Card>
    );
  }

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Group gap="xs">
          <CalendarClock size={16} color="var(--mantine-color-dimmed)" />
          <Title order={4}>{t("employees:card.tabs.career")} · {t("employees:career.workSchedule.title")}</Title>
        </Group>
        {canAssign && !isOwnCard && (
          <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setAssignOpen(true)}>
            {t("employees:career.workSchedule.assign")}
          </Button>
        )}
      </Group>

      {query.isLoading ? (
        <Skeleton h={160} />
      ) : !query.data || query.data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:career.workSchedule.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("employees:career.workSchedule.col.start")}</Table.Th>
                <Table.Th>{t("employees:career.workSchedule.col.end")}</Table.Th>
                <Table.Th>{t("employees:career.workSchedule.col.duration")}</Table.Th>
                <Table.Th>{t("employees:career.workSchedule.col.schedule")}</Table.Th>
                <Table.Th>{t("employees:career.workSchedule.col.assignedAt")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {query.data.map((row: EmployeeWorkScheduleAssignment) => (
                <Table.Tr key={row.id}>
                  <Table.Td className="whitespace-nowrap">
                    <Group gap="xs" wrap="wrap">
                      {date(row.effectiveFrom)}
                      {row.isCurrent && <Badge variant="filled">{t("employees:career.current")}</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td className="whitespace-nowrap">{row.effectiveTo ? date(row.effectiveTo) : "—"}</Table.Td>
                  <Table.Td className="whitespace-nowrap">{formatDuration(row.effectiveFrom, row.effectiveTo, t)}</Table.Td>
                  <Table.Td>{row.workScheduleName}</Table.Td>
                  <Table.Td className="whitespace-nowrap">{date(row.assignedAt.slice(0, 10))}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      {assignOpen && <AssignWorkScheduleDialog employee={employee} onClose={() => setAssignOpen(false)} />}
    </Card>
  );
}

function AssignWorkScheduleDialog({ employee, onClose }: { employee: EmployeeDetail; onClose: () => void }) {
  const { t } = useTranslation(["employees", "attendance", "common"]);
  const queryClient = useQueryClient();
  const [workScheduleId, setWorkScheduleId] = useState<string | null>(null);
  const [effectiveFrom, setEffectiveFrom] = useState(todayDateOnly());
  const [error, setError] = useState<string | null>(null);

  const schedulesQuery = useQuery({
    queryKey: ["attendance", "work-schedules", "active"],
    queryFn: () => listWorkSchedules(false),
  });

  const mutation = useMutation({
    mutationFn: () => assignWorkSchedule(workScheduleId!, [employee.id], effectiveFrom),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["attendance", "work-schedule-assignments", employee.id] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:career.workSchedule.assigned") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  const options = useMemo(() => (schedulesQuery.data ?? []).map((s) => ({ value: s.id, label: s.name })), [schedulesQuery.data]);

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!workScheduleId) return setError(t("employees:career.workSchedule.scheduleRequired"));
    if (!effectiveFrom) return setError(t("employees:career.workSchedule.effectiveFromRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("employees:career.workSchedule.assignTitle")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("employees:career.workSchedule.assignDescription", { name: employee.displayName })}
          </Text>
          <Select
            id="employee-work-schedule-select"
            label={t("employees:career.workSchedule.col.schedule")}
            placeholder={t("employees:career.workSchedule.selectPlaceholder")}
            data={options}
            value={workScheduleId}
            onChange={setWorkScheduleId}
            disabled={mutation.isPending || schedulesQuery.isLoading}
            searchable
          />
          <DatePicker
            id="employee-work-schedule-effective-from"
            value={effectiveFrom}
            disabled={mutation.isPending}
            onChange={setEffectiveFrom}
          />
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
