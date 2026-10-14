import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { PieChart } from "lucide-react";
import { Alert, Button, Card, Loader, Stack, Text, Title } from "@mantine/core";
import { Link } from "react-router";
import { FileUp, LifeBuoy, ListChecks } from "lucide-react";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import { TimeKpiSevenDay, sevenDayRange } from "@/components/attendance/time-kpi-seven-day";
import { TimeKpiLateEarly, TimeKpiOvertime, TimeKpiRate } from "@/components/attendance/time-kpi-groups";
import {
  getAttendanceSevenDay,
  getLateEarlyByUnit,
  getOvertimeByGroup,
  getShiftAttendanceRate,
  type OvertimeGroupBy,
} from "@/services/analytics-time.service";

/** Zaman panosu (Z1.3): 7-day attendance, late/early by unit, overtime by group, shift attendance rate. */
export default function TimeDashboardPage() {
  const { t, i18n } = useTranslation(["attendance-daily", "attendance"]);
  const allowed = useAnyPermission(["attendance.dashboard.view"]);
  const canImport = usePermission("attendance.timesheet.import");
  const canViewDaily = usePermission("attendance.daily.view");
  const [groupBy, setGroupBy] = useState<OvertimeGroupBy>("branch");
  const range = useMemo(() => sevenDayRange(), []);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";

  const sevenDay = useQuery({ queryKey: ["analytics", "time", "attendance-7d", range], queryFn: () => getAttendanceSevenDay(range), enabled: allowed });
  const lateEarly = useQuery({ queryKey: ["analytics", "time", "late-early", range], queryFn: () => getLateEarlyByUnit(range), enabled: allowed });
  const overtime = useQuery({
    queryKey: ["analytics", "time", "overtime", groupBy, range],
    queryFn: () => getOvertimeByGroup(groupBy, range),
    placeholderData: (previous) => previous,
    enabled: allowed,
  });
  const rate = useQuery({ queryKey: ["analytics", "time", "shift-rate", range], queryFn: () => getShiftAttendanceRate(range), enabled: allowed });

  if (!allowed) return <NoAccess />;

  const queries = [sevenDay, lateEarly, overtime, rate];
  const isLoading = queries.some((q) => q.isLoading);
  const failed = queries.some((q) => q.isError);
  const isEmptyScope =
    !isLoading &&
    !failed &&
    (sevenDay.data?.length ?? 0) === 0 &&
    (lateEarly.data?.length ?? 0) === 0 &&
    (overtime.data?.length ?? 0) === 0 &&
    (rate.data?.plannedShiftDays ?? 0) === 0;

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <PieChart size={24} /> {t("attendance-daily:timeDashboard.title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t("attendance-daily:timeDashboard.subtitle")}
        </Text>
      </div>
      {failed && <Alert color="red">{t("attendance-daily:timeDashboard.error")}</Alert>}
      {isLoading && <Loader size="sm" />}
      {isEmptyScope && (
        <Card withBorder padding="xl" radius="md">
          <AttendanceEmptyState
            title={t("attendance-daily:timeDashboard.emptyScope")}
            description={t("attendance-daily:timeDashboard.emptyGuide")}
            action={canImport ? (
              <Button component={Link} to="/app/attendance/device-logs/import" leftSection={<FileUp size={16} />}>
                {t("attendance:openDeviceImport")}
              </Button>
            ) : canViewDaily ? (
              <Button component={Link} to="/app/attendance/daily/all" leftSection={<ListChecks size={16} />}>
                {t("attendance-daily:timeDashboard.openDailyList")}
              </Button>
            ) : (
              <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
                {t("attendance:contactSupport")}
              </Button>
            )}
          />
        </Card>
      )}
      {!isLoading && !failed && !isEmptyScope && (
        <Stack gap="md">
          {rate.data && <TimeKpiRate data={rate.data} locale={locale} />}
          <TimeKpiSevenDay rows={sevenDay.data ?? []} />
          <div className="grid gap-4 lg:grid-cols-2">
            <TimeKpiLateEarly rows={lateEarly.data ?? []} />
            <TimeKpiOvertime rows={overtime.data ?? []} groupBy={groupBy} onGroupByChange={setGroupBy} />
          </div>
        </Stack>
      )}
    </div>
  );
}
