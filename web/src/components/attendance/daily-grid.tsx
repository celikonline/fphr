import { useState } from "react";
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

/** Employee rows of one daily status tab, with manual punch add/delete dialogs (reason mandatory). */
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
              <Table.Th>{t("attendance-daily:dailyList.checkIn")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.checkOut")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.worked")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.overtime")}</Table.Th>
              <Table.Th>{t("attendance-daily:dailyList.anomalies")}</Table.Th>
              {canManage && <Table.Th>{t("attendance-daily:dailyList.actions")}</Table.Th>}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row) => (
              <Table.Tr key={row.employeeId}>
                <Table.Td>{row.employeeName}</Table.Td>
                {showUnit && <Table.Td>{row.orgUnitName || "—"}</Table.Td>}
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
