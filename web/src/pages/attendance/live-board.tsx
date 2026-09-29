import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Card, Group, Table, Text, Title } from "@mantine/core";
import { MonitorCheck } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getLiveAttendanceBoard } from "@/services/attendance.service";

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

  const rows = data ?? [];
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

      <Card withBorder padding={0}>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("attendance:liveBoard.employee")}</Table.Th>
              <Table.Th>{t("attendance:liveBoard.status")}</Table.Th>
              <Table.Th>{t("attendance:day.checkIn")}</Table.Th>
              <Table.Th>{t("attendance:day.checkOut")}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {isLoading ? (
              <Table.Tr>
                <Table.Td colSpan={4}>
                  <Text size="sm" c="dimmed">
                    {t("common:loading")}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ) : rows.length === 0 ? (
              <Table.Tr>
                <Table.Td colSpan={4}>
                  <Text size="sm" c="dimmed" ta="center" p="lg">
                    {t("attendance:liveBoard.empty")}
                  </Text>
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
                </Table.Tr>
              ))
            )}
          </Table.Tbody>
        </Table>
      </Card>
    </div>
  );
}
