import { Fragment, useState } from "react";
import { useTranslation } from "react-i18next";
import { Plus, Trash2 } from "lucide-react";
import { ActionIcon, Alert, Badge, Button, Group, Modal, Select, Stack, Table, Text, TextInput, Textarea, Tooltip } from "@mantine/core";
import { useLeaveName } from "@/components/leave/leave-shared";
import { IncalculableHint } from "@/components/attendance/incalculable-hint";
import { getAttendanceErrorMessage } from "@/lib/attendance-error";
import type { DailyAttendanceRow, ManualPunchInput, PunchDirection } from "@/services/attendance-daily.service";

interface DailyGridProps {
  rows: DailyAttendanceRow[];
  /** attendance.punch.manage: shows the add/delete punch actions. */
  canManage: boolean;
  onAddPunch: (input: ManualPunchInput) => Promise<unknown>;
  onDeletePunch: (id: string, reason: string) => Promise<unknown>;
}

const hhmm = (value?: string | null) => (value ? value.slice(0, 5) : "—");

type Dialog =
  | { kind: "add"; row: DailyAttendanceRow }
  | { kind: "delete"; id: string; time: string };

/** Daily employee rows with expandable punch detail and audited manual punch add/delete actions. */
export default function DailyGrid({ rows, canManage, onAddPunch, onDeletePunch }: DailyGridProps) {
  const { t } = useTranslation(["attendance-daily", "attendance"]);
  const leaveName = useLeaveName();
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [reason, setReason] = useState("");
  const [time, setTime] = useState("09:00");
  const [direction, setDirection] = useState<PunchDirection>("in");
  const [reasonError, setReasonError] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [selectedEmployeeId, setSelectedEmployeeId] = useState<string | null>(null);

  function open(next: Dialog) {
    setReason("");
    setTime("09:00");
    setDirection("in");
    setReasonError(false);
    setServerError(null);
    setDialog(next);
  }

  async function submit() {
    if (!dialog) return;
    const trimmed = reason.trim();
    if (!trimmed) {
      setReasonError(true);
      return;
    }
    setBusy(true);
    setServerError(null);
    try {
      if (dialog.kind === "add") {
        const occurredAtUtc = new Date(`${dialog.row.date}T${time}:00`).toISOString();
        await onAddPunch({ employeeId: dialog.row.employeeId, occurredAtUtc, direction, reason: trimmed });
      } else {
        await onDeletePunch(dialog.id, trimmed);
      }
      setDialog(null);
    } catch (error) {
      setServerError(getAttendanceErrorMessage(error, t("attendance-daily:dailyList.genericError"), t));
    } finally {
      setBusy(false);
    }
  }

  const showUnit = rows.some((row) => Boolean(row.orgUnitName));

  const deleteButton = (id: string | null | undefined, time: string, label: string) =>
    id ? (
      <Tooltip label={label}>
        <ActionIcon variant="subtle" color="red" size="sm" aria-label={label} onClick={() => open({ kind: "delete", id, time })}>
          <Trash2 className="h-3.5 w-3.5" />
        </ActionIcon>
      </Tooltip>
    ) : null;

  return (
    <>
      <Table.ScrollContainer minWidth={720}>
        <Table verticalSpacing="xs" highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("attendance-daily:dailyList.employee")}</Table.Th>
              {showUnit && <Table.Th>{t("attendance-daily:dailyList.unit")}</Table.Th>}
              <Table.Th>{t("attendance-daily:dailyList.planned")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.checkIn")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.checkOut")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.worked")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.overtime")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.anomalies")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.statuses")}</Table.Th>
              {canManage && <Table.Th>{t("attendance-daily:dailyList.actions")}</Table.Th>}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row) => (
              <Fragment key={row.employeeId}>
              <Table.Tr style={{ background: selectedEmployeeId === row.employeeId ? "var(--mantine-color-blue-light)" : undefined }}>
                <Table.Td>
                  <Button aria-expanded={selectedEmployeeId === row.employeeId} variant="subtle" size="compact-sm" px={0} onClick={() => setSelectedEmployeeId((id) => id === row.employeeId ? null : row.employeeId)}>
                    {row.employeeName}
                  </Button>
                </Table.Td>
                {showUnit && <Table.Td>{row.orgUnitName || "—"}</Table.Td>}
                <Table.Td>{row.plannedStart && row.plannedEnd ? `${hhmm(row.plannedStart)}–${hhmm(row.plannedEnd)}` : "—"}</Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    {hhmm(row.checkIn)}
                    {canManage && deleteButton(row.checkInPunchId, hhmm(row.checkIn), t("attendance-daily:dailyList.deleteCheckIn"))}
                  </Group>
                </Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    {hhmm(row.checkOut)}
                    {canManage && deleteButton(row.checkOutPunchId, hhmm(row.checkOut), t("attendance-daily:dailyList.deleteCheckOut"))}
                  </Group>
                </Table.Td>
                <Table.Td>{t("attendance-daily:dailyList.minutes", { count: row.workedMinutes })}</Table.Td>
                <Table.Td>{row.overtimeMinutes > 0 ? t("attendance-daily:dailyList.minutes", { count: row.overtimeMinutes }) : "—"}</Table.Td>
                <Table.Td>
                  <Group gap={4}>
                    <IncalculableHint reason={row.incalculableReason} />
                    {row.leaveType && <Badge variant="light" color="blue">{leaveName(row.leaveType)}</Badge>}
                    {row.anomalies.map((a) => (
                      <Badge key={a} variant="light" color="orange">{t(`attendance:anomaly.${a}`, { defaultValue: a })}</Badge>
                    ))}
                  </Group>
                </Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="wrap">
                    {(row.statuses ?? []).map((status) => (
                      <Badge key={status} variant="light" color={status === "absent" || status === "incalculable" ? "red" : status === "onLeave" ? "blue" : status === "overtime" ? "orange" : "teal"}>
                        {t(`attendance-daily:dailyList.status.${status}`)}
                      </Badge>
                    ))}
                  </Group>
                </Table.Td>
                {canManage && (
                  <Table.Td>
                    <Tooltip label={t("attendance-daily:dailyList.addPunch")}>
                      <ActionIcon variant="subtle" aria-label={t("attendance-daily:dailyList.addPunch")} onClick={() => open({ kind: "add", row })}>
                        <Plus className="h-4 w-4" />
                      </ActionIcon>
                    </Tooltip>
                  </Table.Td>
                )}
              </Table.Tr>
              {selectedEmployeeId === row.employeeId && (
                <Table.Tr>
                  <Table.Td colSpan={showUnit ? (canManage ? 10 : 9) : (canManage ? 9 : 8)} bg="var(--mantine-color-gray-light)">
                    <Stack gap="xs" py="xs">
                      <Group justify="space-between">
                        <div>
                          <Text fw={600} size="sm">{row.employeeName} · {t("attendance-daily:dailyList.movements")}</Text>
                          <Text size="xs" c="dimmed">{row.date}</Text>
                        </div>
                        {canManage && <Button size="xs" leftSection={<Plus size={14} />} onClick={() => open({ kind: "add", row })}>{t("attendance-daily:dailyList.addPunch")}</Button>}
                      </Group>
                      {(row.punches?.length ?? 0) === 0 ? (
                        <Text size="sm" c="dimmed">{t("attendance-daily:dailyList.noMovements")}</Text>
                      ) : (
                        <Table withTableBorder withColumnBorders verticalSpacing="xs" bg="var(--mantine-color-body)">
                          <Table.Thead><Table.Tr>
                            <Table.Th>{t("attendance-daily:dailyList.movementTime")}</Table.Th>
                            <Table.Th>{t("attendance-daily:dailyList.direction")}</Table.Th>
                            <Table.Th>{t("attendance-daily:dailyList.source")}</Table.Th>
                            <Table.Th>{t("attendance-daily:dailyList.reason")}</Table.Th>
                            {canManage && <Table.Th>{t("attendance-daily:dailyList.actions")}</Table.Th>}
                          </Table.Tr></Table.Thead>
                          <Table.Tbody>{row.punches?.map((punch) => (
                            <Table.Tr key={punch.id}>
                              <Table.Td>{new Date(punch.occurredAtUtc).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" })}</Table.Td>
                              <Table.Td>{t(`attendance-daily:dailyList.directions.${punch.direction}`, { defaultValue: punch.direction })}</Table.Td>
                              <Table.Td>{t(`attendance-daily:dailyList.sources.${punch.source}`, { defaultValue: punch.source })}</Table.Td>
                              <Table.Td>{punch.reason || "—"}</Table.Td>
                              {canManage && <Table.Td>{deleteButton(punch.id, new Date(punch.occurredAtUtc).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" }), t("attendance-daily:dailyList.deleteMovement"))}</Table.Td>}
                            </Table.Tr>
                          ))}</Table.Tbody>
                        </Table>
                      )}
                    </Stack>
                  </Table.Td>
                </Table.Tr>
              )}
              </Fragment>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal
        opened={dialog !== null}
        onClose={() => setDialog(null)}
        title={dialog?.kind === "add" ? t("attendance-daily:dailyList.addPunchTitle") : t("attendance-daily:dailyList.deleteTitle")}
      >
        <Stack gap="sm">
          {dialog?.kind === "add" && (
            <Group grow align="flex-start">
              <TextInput type="time" label={t("attendance-daily:dailyList.time")} value={time} onChange={(e) => setTime(e.currentTarget.value)} />
              <Select
                label={t("attendance-daily:dailyList.direction")}
                allowDeselect={false}
                value={direction}
                onChange={(v) => setDirection((v as PunchDirection) ?? "in")}
                data={[
                  { value: "in", label: t("attendance-daily:dailyList.dirIn") },
                  { value: "out", label: t("attendance-daily:dailyList.dirOut") },
                ]}
              />
            </Group>
          )}
          {dialog?.kind === "delete" && (
            <Text size="sm">{t("attendance-daily:dailyList.deleteConfirmText", { time: dialog.time })}</Text>
          )}
          <Textarea
            label={t("attendance-daily:dailyList.reason")}
            withAsterisk
            value={reason}
            onChange={(e) => {
              setReason(e.currentTarget.value);
              setReasonError(false);
            }}
            error={reasonError ? t("attendance-daily:dailyList.reasonRequired") : undefined}
          />
          {serverError && <Alert color="red">{serverError}</Alert>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setDialog(null)}>{t("attendance-daily:dailyList.cancel")}</Button>
            <Button color={dialog?.kind === "delete" ? "red" : undefined} loading={busy} onClick={() => void submit()}>
              {dialog?.kind === "delete" ? t("attendance-daily:dailyList.confirmDelete") : t("attendance-daily:dailyList.save")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
