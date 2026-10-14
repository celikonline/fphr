import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { TFunction } from "i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Table, Text, Title } from "@mantine/core";
import { FileUp, LifeBuoy, MonitorCheck } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import { getLiveAttendanceBoard, type LiveAttendanceStatus } from "@/services/attendance.service";
import { getLiveTerminals, type LiveBoardLastPunch, type LiveTerminalStatus } from "@/services/attendance-board.service";

const LIVE_BOARD_POLL_MS = 30_000;

/**
 * Canlı içeride/dışarıda panosu (S-31 kabul kriteri 3): o an kim işte kim değil. Ofis ekranına yansıtılabilecek
 * sade bir liste; polling ile yenilenir (WebSocket gerekmiyor). attendance.board.view.
 */
export default function LiveBoardPage() {
  const canView = usePermission("attendance.board.view");
  if (!canView) return <NoAccess />;
  return <LiveBoard />;
}

function LiveBoard() {
  const { t } = useTranslation(["attendance", "common"]);

  const { data, isLoading } = useQuery({
    queryKey: ["attendance", "live-board"],
    queryFn: () => getLiveAttendanceBoard(),
    refetchInterval: LIVE_BOARD_POLL_MS,
  });

  const canViewTerminals = usePermission("attendance.terminal.view");
  const canImport = usePermission("attendance.timesheet.import");
  const canViewDaily = usePermission("attendance.daily.view");
  const { data: terminals } = useQuery({
    queryKey: ["attendance", "live-board", "terminals"],
    queryFn: () => getLiveTerminals(),
    refetchInterval: LIVE_BOARD_POLL_MS,
    enabled: canViewTerminals,
  });

  const rows: Array<LiveAttendanceStatus & LiveBoardLastPunch> = data ?? [];
  const insideCount = rows.filter((r) => r.isInside).length;

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <MonitorCheck size={24} /> {t("attendance:liveBoard.title")}
          </Title>
          <Text c="dimmed">{t("attendance:liveBoard.subtitle")}</Text>
        </div>
        <Badge color="green" size="lg">
          {t("attendance:liveBoard.insideCount", { count: insideCount, total: rows.length })}
        </Badge>
      </Group>

      {canViewTerminals && terminals && <TerminalStrip terminals={terminals} />}

      <Card withBorder padding={0}>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("attendance:liveBoard.employee")}</Table.Th>
              <Table.Th>{t("attendance:liveBoard.status")}</Table.Th>
              <Table.Th>{t("attendance:day.checkIn")}</Table.Th>
              <Table.Th>{t("attendance:day.checkOut")}</Table.Th>
              <Table.Th>{t("attendance:liveBoard.lastPunch")}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {isLoading ? (
              <Table.Tr>
                <Table.Td colSpan={5}>
                  <Text size="sm" c="dimmed">
                    {t("common:loading")}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ) : rows.length === 0 ? (
              <Table.Tr>
                <Table.Td colSpan={5}>
                  <AttendanceEmptyState
                    title={t("attendance:liveBoard.empty")}
                    description={t("attendance:liveBoard.emptyGuide")}
                    action={canImport ? (
                      <Button component={Link} to="/app/attendance/device-logs/import" leftSection={<FileUp size={16} />}>
                        {t("attendance:openDeviceImport")}
                      </Button>
                    ) : canViewDaily ? (
                      <Button component={Link} to="/app/attendance/daily/all">{t("attendance:openDailyList")}</Button>
                    ) : (
                      <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
                        {t("attendance:contactSupport")}
                      </Button>
                    )}
                  />
                </Table.Td>
              </Table.Tr>
            ) : (
              rows.map((row) => (
                <Table.Tr key={row.employeeId}>
                  <Table.Td>{row.employeeName}</Table.Td>
                  <Table.Td>
                    <Badge color={row.isInside ? "green" : "gray"} variant="light">
                      {row.isInside ? t("attendance:liveBoard.inside") : t("attendance:liveBoard.outside")}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{row.checkIn?.slice(0, 5) ?? "—"}</Table.Td>
                  <Table.Td>{row.checkOut?.slice(0, 5) ?? "—"}</Table.Td>
                  <Table.Td>{lastPunchLabel(row, t)}</Table.Td>
                </Table.Tr>
              ))
            )}
          </Table.Tbody>
        </Table>
      </Card>
    </div>
  );
}

/** Eski API yanıtlarında son okutma alanları yoktur; o zaman "—". */
function lastPunchLabel(row: LiveBoardLastPunch, t: TFunction): string {
  if (!row.lastPunchSource) return "—";
  const source = t(`attendance:liveBoard.sources.${row.lastPunchSource}`);
  return row.lastPunchTerminalName ? `${source} · ${row.lastPunchTerminalName}` : source;
}

/** Terminal durumu: rozet rengin yanında metin de taşır (renk tek başına anlam vermez). */
function TerminalStrip({ terminals }: { terminals: LiveTerminalStatus[] }) {
  const { t } = useTranslation(["attendance"]);
  return (
    <section aria-label={t("attendance:liveBoard.terminals.title")} className="space-y-2">
      <Title order={2} size="h4">
        {t("attendance:liveBoard.terminals.title")}
      </Title>
      {terminals.length === 0 ? (
        <AttendanceEmptyState
          title={t("attendance:liveBoard.terminals.empty")}
          description={t("attendance:liveBoard.terminals.emptyGuide")}
          action={<Button component={Link} to="/app/attendance/terminals">{t("attendance:liveBoard.terminals.openTerminals")}</Button>}
        />
      ) : (
        <Group gap="sm" align="stretch">
          {terminals.map((terminal) => (
            <Card key={terminal.terminalId} withBorder padding="sm">
              <Group gap="xs" justify="space-between" wrap="nowrap">
                <Text fw={600}>{terminal.name}</Text>
                <Badge color={terminal.online ? "green" : "red"} variant="light">
                  {terminal.online ? t("attendance:liveBoard.terminals.online") : t("attendance:liveBoard.terminals.offline")}
                </Badge>
              </Group>
              <Text size="xs" c="dimmed">
                {[terminal.groupName, terminal.isActive ? null : t("attendance:liveBoard.terminals.inactive")].filter(Boolean).join(" · ") || terminal.code}
              </Text>
              <Text size="xs">{t("attendance:liveBoard.terminals.todayCount", { count: terminal.todayPunchCount })}</Text>
              <Text size="xs" c="dimmed">
                {t("attendance:liveBoard.terminals.lastSeen")}:{" "}
                {terminal.lastSeenAtUtc ? new Date(terminal.lastSeenAtUtc).toLocaleTimeString() : t("attendance:liveBoard.terminals.never")}
              </Text>
            </Card>
          ))}
        </Group>
      )}
    </section>
  );
}
