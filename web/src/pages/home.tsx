import { useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Anchor, Avatar, Badge, Box, Card, Divider, Group, Progress, Skeleton, Stack, Text, Title } from "@mantine/core";
import { DonutChart } from "@mantine/charts";
import { CalendarHeart, CalendarRange, Cake, Check, Clock, Plane, PieChart, RefreshCw, Users, X } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { initials } from "@/components/employees/employee-badges";
import { useOrgLevels, useOrgUnits } from "@/components/employees/use-org-units";
import { useDays, useLeaveName, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { getDepartmentDistribution, getMyCareer, getMyEmployee } from "@/services/employees.service";
import { getUpcomingBirthdays, getUpcomingHolidays } from "@/services/home.service";
import { getLeaveCalendar, getMyLeaveBalances, getMyLeaveRequests, type LeaveRequestStatus } from "@/services/leave.service";
import { getApiErrorStatus } from "@/lib/api-error";
import { daysUntil, formatDateOnly, toDateOnly } from "@/lib/dates";

const HOLIDAY_DAYS = 90;
const BIRTHDAY_DAYS = 30;
const UPCOMING_LEAVE_DAYS = 30;
const ICON_COLOR = "var(--mantine-primary-color-filled)";
const DEPARTMENT_COLORS = ["blue", "green", "orange", "grape", "cyan", "pink", "yellow", "teal", "indigo", "red"];

/** 403/404 are expected (permission, access setting or module not deployed) - don't retry them. */
function retryUnlessClientError(failureCount: number, error: unknown): boolean {
  const status = getApiErrorStatus(error);
  if (status === 401 || status === 403 || status === 404) return false;
  return failureCount < 1;
}

/** First letter of the first and last word of a full name (colleague rows only have a display name, not first/last). */
function nameInitials(displayName: string): string {
  const parts = displayName.trim().split(/\s+/);
  return `${parts[0]?.charAt(0) ?? ""}${parts[parts.length - 1]?.charAt(0) ?? ""}`.toLocaleUpperCase("tr") || "?";
}

function WidgetIcon({ icon }: { icon: ReactNode }) {
  return (
    <span
      className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg"
      style={{ backgroundColor: "var(--mantine-primary-color-light)" }}
    >
      {icon}
    </span>
  );
}

function WidgetCard({ title, icon, badge, children }: { title: string; icon: ReactNode; badge?: ReactNode; children: ReactNode }) {
  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="sm" mb="sm" wrap="nowrap">
        <Group gap="sm" wrap="nowrap">
          <WidgetIcon icon={icon} />
          <Title order={5}>{title}</Title>
        </Group>
        {badge}
      </Group>
      <div className="flex-1">{children}</div>
    </Card>
  );
}

function WidgetMessage({ children }: { children: ReactNode }) {
  return (
    <Text size="sm" c="dimmed" ta="center" py="md">
      {children}
    </Text>
  );
}

function WidgetSkeleton() {
  return (
    <Stack gap="xs">
      <Skeleton h={24} />
      <Skeleton h={24} />
      <Skeleton h={24} w="75%" />
    </Stack>
  );
}

function ViewAllLink({ to }: { to: string }) {
  const { t } = useTranslation(["home"]);
  return (
    <Anchor component={Link} to={to} size="sm" ta="right" className="block">
      {t("home:viewAll")}
    </Anchor>
  );
}

function HolidaysWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-holidays", HOLIDAY_DAYS],
    queryFn: () => getUpcomingHolidays(HOLIDAY_DAYS),
    retry: retryUnlessClientError,
  });

  return (
    <WidgetCard title={t("home:holidays.title")} icon={<CalendarHeart size={18} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:holidays.empty", { days: HOLIDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:holidays.empty", { days: HOLIDAY_DAYS })}</WidgetMessage>
      ) : (
        <ul>
          {data.map((holiday, index) => (
            <li key={`${holiday.holidayCalendarId}-${holiday.date}-${holiday.name}`}>
              {index > 0 && <Divider />}
              <Group justify="space-between" gap="sm" wrap="nowrap" py="xs">
                <Text size="sm" fw={500} truncate>
                  {holiday.name}
                </Text>
                <Group gap={6} wrap="nowrap" className="shrink-0">
                  <Text size="xs" c="dimmed">
                    {daysUntil(holiday.date)}
                    {t("home:daysSuffix")}
                  </Text>
                  <Text size="xs" fw={500}>
                    {formatDateOnly(holiday.date, i18n.language, { day: "numeric", month: "short", year: "numeric" })}
                  </Text>
                </Group>
              </Group>
            </li>
          ))}
        </ul>
      )}
    </WidgetCard>
  );
}

function BirthdaysWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-birthdays", BIRTHDAY_DAYS],
    queryFn: () => getUpcomingBirthdays(BIRTHDAY_DAYS),
    retry: retryUnlessClientError,
  });

  return (
    <WidgetCard title={t("home:birthdays.title")} icon={<Cake size={18} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:birthdays.empty", { days: BIRTHDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:birthdays.empty", { days: BIRTHDAY_DAYS })}</WidgetMessage>
      ) : (
        <ul>
          {data.map((birthday, index) => (
            <li key={birthday.id}>
              {index > 0 && <Divider />}
              <Group justify="space-between" gap="sm" wrap="nowrap" py="xs">
                <Group gap="sm" wrap="nowrap" className="min-w-0">
                  <Avatar size={28} radius="xl">
                    {nameInitials(birthday.displayName)}
                  </Avatar>
                  <Anchor component={Link} to="/app/employees" size="sm" fw={500} truncate underline="hover">
                    {birthday.displayName}
                  </Anchor>
                </Group>
                <Text size="xs" fw={500} className="shrink-0">
                  {formatDateOnly(birthday.nextBirthday, i18n.language, { day: "numeric", month: "short" })}
                </Text>
              </Group>
            </li>
          ))}
        </ul>
      )}
    </WidgetCard>
  );
}

/** Own employee card ("Home.ShowOrgInfo"): name, ünvan/departman (career assignments), manager, hire date. Headerless, like the reference dashboard's personnel card. */
function OrgInfoWidget() {
  const { t, i18n } = useTranslation(["home", "navigation"]);
  const { data: employee, isLoading, error } = useQuery({
    queryKey: ["home", "org-info"],
    queryFn: getMyEmployee,
    retry: retryUnlessClientError,
  });
  const { data: career } = useQuery({
    queryKey: ["home", "org-info", "career"],
    queryFn: getMyCareer,
    enabled: Boolean(employee),
  });
  const { levels } = useOrgLevels();
  const { nameById } = useOrgUnits();

  const current = career?.find((record) => record.isCurrent) ?? null;
  const levelUnitName = (code: string) => {
    const level = levels.find((l) => l.code === code);
    const assignment = level ? current?.assignments.find((a) => a.orgUnitTypeId === level.id) : undefined;
    return assignment ? nameById.get(assignment.orgUnitId) : undefined;
  };
  const title = levelUnitName("Title");
  const department = levelUnitName("Department");

  return (
    <Card withBorder padding="lg">
      {isLoading ? (
        <WidgetSkeleton />
      ) : error || !employee ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : (
        <Stack gap="md">
          <Group justify="space-between" gap="md" wrap="nowrap" align="flex-start">
            <div className="min-w-0 flex-1">
              <Text fw={700} size="lg" truncate>
                {employee.displayName}
              </Text>
              {title && (
                <Text size="sm" c="dimmed" truncate>
                  {title}
                </Text>
              )}
              {department && (
                <Text size="sm" c="dimmed" truncate>
                  {department}
                </Text>
              )}
            </div>
            <Avatar size={64} radius="md">
              {initials(employee.firstName, employee.lastName)}
            </Avatar>
          </Group>
          <Stack gap={6}>
            <Group justify="space-between" gap="sm">
              <Text size="sm" c="dimmed">
                {t("home:orgInfo.manager")}
              </Text>
              {employee.managerEmployeeId ? (
                <Anchor component={Link} to={`/app/employees/${employee.managerEmployeeId}`} size="sm" fw={500} underline="hover">
                  {employee.managerDisplayName ?? t("home:orgInfo.unknownManager")}
                </Anchor>
              ) : (
                <Text size="sm" fw={500}>
                  —
                </Text>
              )}
            </Group>
            <Group justify="space-between" gap="sm">
              <Text size="sm" c="dimmed">
                {t("home:orgInfo.hireDate")}
              </Text>
              <Text size="sm" fw={500}>
                {employee.hireDate ? formatDateOnly(employee.hireDate, i18n.language) : "—"}
              </Text>
            </Group>
          </Stack>
          <ViewAllLink to="/app/account" />
        </Stack>
      )}
    </Card>
  );
}

const STATUS_ICON: Record<LeaveRequestStatus, { icon: ReactNode; color: string }> = {
  approved: { icon: <Check size={12} />, color: "green" },
  pending: { icon: <Clock size={12} />, color: "gray" },
  rejected: { icon: <X size={12} />, color: "red" },
  withdrawn: { icon: <X size={12} />, color: "gray" },
  cancelled: { icon: <X size={12} />, color: "gray" },
};

function LeaveStatusIcon({ status }: { status: LeaveRequestStatus }) {
  const { icon, color } = STATUS_ICON[status];
  return (
    <span
      className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-white"
      style={{ backgroundColor: `var(--mantine-color-${color}-6)` }}
    >
      {icon}
    </span>
  );
}

/** Own leave balances (per type, with a 3-way progress bar: available/future/used) plus the last 4 own requests. */
function LeaveInfoWidget() {
  const { t, i18n } = useTranslation(["home", "leave"]);
  const days = useDays();
  const leaveName = useLeaveName();
  const { data: balances, isLoading, error, refetch, isFetching } = useQuery({
    queryKey: ["home", "leave-balances"],
    queryFn: getMyLeaveBalances,
    retry: retryUnlessClientError,
  });
  // Own requests need their own permission; without it (or on error) the "recent requests" part is hidden.
  const canViewRequests = usePermission("leave.my.requests");
  const { data: requests, error: requestsError } = useQuery({
    queryKey: ["home", "leave-requests"],
    queryFn: getMyLeaveRequests,
    enabled: canViewRequests,
    retry: retryUnlessClientError,
  });
  const showRecentRequests = canViewRequests && !requestsError;
  const typeName = useLeaveTypeNames(balances, undefined);
  const recentRequests = useMemo(
    () => [...(requests ?? [])].sort((a, b) => b.startDate.localeCompare(a.startDate)).slice(0, 4),
    [requests],
  );

  return (
    <WidgetCard
      title={t("home:leaveInfo.title")}
      icon={<Plane size={18} color={ICON_COLOR} />}
      badge={
        <ActionIcon variant="subtle" color="gray" onClick={() => refetch()} loading={isFetching} aria-label={t("home:refresh")}>
          <RefreshCw size={16} />
        </ActionIcon>
      }
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : !balances || balances.balances.length === 0 ? (
        <WidgetMessage>{t("home:leaveInfo.empty")}</WidgetMessage>
      ) : (
        <Stack gap="lg">
          {balances.balances.map((item) => {
            const b = item.balance;
            const pool = b.availableBalance + b.futureApprovedInPeriod + b.totalUsed;
            const pct = (value: number) => (pool <= 0 ? 0 : Math.max(0, Math.min(100, (value / pool) * 100)));
            return (
              <Stack key={b.leaveTypeId} gap="xs">
                <Text size="sm" c="dimmed">
                  {leaveName(item.leaveTypeName)}
                </Text>
                <Text size="xs" c="dimmed">
                  {t("home:leaveInfo.availableUntil", { date: formatDateOnly(b.currentPeriodTo, i18n.language) })}
                </Text>
                <Text size="xl" fw={700} c="green.7">
                  {days(b.availableBalance)}{" "}
                  <Text span size="sm" fw={500} c="green.7">
                    {t("leave:days")}
                  </Text>
                </Text>
                <Progress.Root size="md">
                  <Progress.Section value={pct(b.availableBalance)} color="green" />
                  <Progress.Section value={pct(b.futureApprovedInPeriod)} color="orange" />
                  <Progress.Section value={pct(b.totalUsed)} color="red" />
                </Progress.Root>
                <Group gap="lg">
                  <Group gap={6} wrap="nowrap">
                    <span className="h-2 w-2 rounded-full" style={{ backgroundColor: "var(--mantine-color-orange-6)" }} />
                    <Text size="xs" c="dimmed">
                      {t("home:leaveInfo.future")} {days(b.futureApprovedInPeriod)}
                    </Text>
                  </Group>
                  <Group gap={6} wrap="nowrap">
                    <span className="h-2 w-2 rounded-full" style={{ backgroundColor: "var(--mantine-color-red-6)" }} />
                    <Text size="xs" c="dimmed">
                      {t("home:leaveInfo.used")} {days(b.totalUsed)}
                    </Text>
                  </Group>
                </Group>
              </Stack>
            );
          })}

          {showRecentRequests && recentRequests.length > 0 && (
            <Stack gap={0}>
              {recentRequests.map((request, index) => (
                <div key={request.id}>
                  {index > 0 && <Divider />}
                  <Group justify="space-between" gap="sm" py="xs" wrap="nowrap">
                    <Group gap="sm" wrap="nowrap" className="min-w-0">
                      <LeaveStatusIcon status={request.status} />
                      <Text size="sm" fw={500} truncate>
                        {typeName(request)}
                      </Text>
                    </Group>
                    <Text size="xs" c="dimmed" className="shrink-0 whitespace-nowrap">
                      {days(request.calculatedDays)}
                      {t("home:daysSuffix")} {formatDateOnly(request.startDate, i18n.language, { day: "numeric", month: "short" })}
                    </Text>
                  </Group>
                </div>
              ))}
            </Stack>
          )}

          <ViewAllLink to="/app/leave" />
        </Stack>
      )}
    </WidgetCard>
  );
}

/** Colleagues' upcoming/pending leave in the next `UPCOMING_LEAVE_DAYS` days. */
function UpcomingLeavesWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const days = useDays();
  // Computed once per mount: reading the clock during render is impure (react-hooks/purity).
  const [{ from, to }] = useState(() => {
    const today = new Date();
    const end = new Date(today.getFullYear(), today.getMonth(), today.getDate() + UPCOMING_LEAVE_DAYS);
    return { from: toDateOnly(today), to: toDateOnly(end) };
  });
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-leaves", from, to],
    queryFn: () => getLeaveCalendar({ from, to }),
    retry: retryUnlessClientError,
  });
  const entries = useMemo(() => [...(data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate)), [data]);

  return (
    <WidgetCard title={t("home:leaves.title")} icon={<Plane size={18} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:leaves.empty", { days: UPCOMING_LEAVE_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : entries.length === 0 ? (
        <WidgetMessage>{t("home:leaves.empty", { days: UPCOMING_LEAVE_DAYS })}</WidgetMessage>
      ) : (
        <Stack gap={0}>
          <ul>
            {entries.slice(0, 6).map((entry, index) => {
              const inDays = daysUntil(entry.startDate);
              return (
                <li key={entry.requestId}>
                  {index > 0 && <Divider />}
                  <Group justify="space-between" gap="sm" wrap="nowrap" py="xs">
                    <Group gap="sm" wrap="nowrap" className="min-w-0">
                      <Avatar size={28} radius="xl">
                        {nameInitials(entry.employeeName)}
                      </Avatar>
                      <Text size="sm" fw={500} truncate>
                        {entry.employeeName}
                      </Text>
                    </Group>
                    <Group gap={6} wrap="nowrap" className="shrink-0">
                      <Text size="xs" c="dimmed">
                        {days(entry.calculatedDays)}
                        {t("home:daysSuffix")}
                      </Text>
                      <Text size="xs" fw={500}>
                        {inDays <= 0 ? t("home:today") : formatDateOnly(entry.startDate, i18n.language, { day: "numeric", month: "short" })}
                      </Text>
                    </Group>
                  </Group>
                </li>
              );
            })}
          </ul>
          <ViewAllLink to="/app/leave/calendar" />
        </Stack>
      )}
    </WidgetCard>
  );
}

/** Active headcount per department, from the `department-distribution` endpoint; center label shows the largest department. */
function DepartmentDistributionWidget() {
  const { t } = useTranslation(["home"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "department-distribution"],
    queryFn: getDepartmentDistribution,
    retry: retryUnlessClientError,
  });

  const chartData = useMemo(
    () =>
      (data ?? []).map((item, index) => ({
        key: item.orgUnitId ?? "unassigned",
        name: item.departmentName ?? t("home:departmentDistribution.unassigned"),
        value: item.count,
        color: DEPARTMENT_COLORS[index % DEPARTMENT_COLORS.length],
      })),
    [data, t],
  );
  const total = chartData.reduce((sum, item) => sum + item.value, 0);
  const largest = chartData[0];

  return (
    <WidgetCard title={t("home:departmentDistribution.title")} icon={<PieChart size={18} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : total === 0 ? (
        <WidgetMessage>{t("home:departmentDistribution.empty")}</WidgetMessage>
      ) : (
        <Stack align="center" gap="md">
          <Box pos="relative" w={160} h={160}>
            <DonutChart data={chartData} size={160} thickness={22} withTooltip={false} />
            <Stack gap={0} align="center" justify="center" pos="absolute" top={0} left={0} right={0} bottom={0}>
              <Text size="xs" c="dimmed" truncate maw={110} ta="center">
                {largest?.name}
              </Text>
              <Text size="xl" fw={700}>
                {largest?.value}
              </Text>
            </Stack>
          </Box>
          <Group gap={6} className="w-full">
            <Users size={16} color="var(--mantine-color-dimmed)" />
            <Text size="sm" fw={600}>
              {total}
            </Text>
          </Group>
        </Stack>
      )}
    </WidgetCard>
  );
}

function PlannedWidget({ title, icon, phase, description }: { title: string; icon: ReactNode; phase: number; description: string }) {
  const { t } = useTranslation(["navigation"]);
  return (
    <WidgetCard
      title={title}
      icon={icon}
      badge={
        <Badge variant="outline" color="gray" className="shrink-0">
          {t("navigation:phase", { phase })}
        </Badge>
      }
    >
      <WidgetMessage>{description}</WidgetMessage>
    </WidgetCard>
  );
}

export default function HomePage() {
  const { t, i18n } = useTranslation(["home", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const canViewHolidays = usePermission("organization.holidays.view");
  const canViewDirectory = usePermission("employee.directory.view");
  const canViewLeaveBalances = usePermission("leave.my.balances");
  // The leave calendar endpoint also requires the Calendar.ShowMenu access setting.
  const hasLeaveCalendarPermission = usePermission("leave.my.calendar");
  const showCalendarMenu = useAccessSetting("showCalendarMenu");
  const canViewLeaveCalendar = hasLeaveCalendarPermission && showCalendarMenu;
  const showOrgInfo = useAccessSetting("showOrgInfo");
  const showBirthdays = useAccessSetting("showUpcomingBirthdays");
  const showLeaves = useAccessSetting("showUpcomingLeaves");
  const showEvents = useAccessSetting("showEventCalendar");

  const hasAnyWidget =
    showOrgInfo ||
    canViewHolidays ||
    canViewDirectory ||
    canViewLeaveBalances ||
    (showLeaves && canViewLeaveCalendar) ||
    showEvents;

  // Computed once per mount: reading the clock during render is impure (react-hooks/purity).
  const [today] = useState(() => new Date());
  const todayLabel = new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "long", year: "numeric" }).format(today);
  const weekdayLabel = new Intl.DateTimeFormat(i18n.language, { weekday: "long" }).format(today);

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Text size="sm" c="dimmed">
          {t("home:greeting", { name: user?.firstName || user?.name || "" })}
        </Text>
        <Title order={2}>
          {t("home:today")} {todayLabel}, {weekdayLabel}
        </Title>
      </div>

      {hasAnyWidget ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {showOrgInfo && <OrgInfoWidget />}
          {canViewLeaveBalances && <LeaveInfoWidget />}
          {showLeaves && canViewLeaveCalendar && <UpcomingLeavesWidget />}
          {canViewDirectory && <DepartmentDistributionWidget />}
          {showBirthdays && canViewDirectory && <BirthdaysWidget />}
          {canViewHolidays && <HolidaysWidget />}
          {showEvents && (
            <PlannedWidget
              title={t("home:events.title")}
              icon={<CalendarRange size={18} color={ICON_COLOR} />}
              phase={4}
              description={t("home:events.planned")}
            />
          )}
        </div>
      ) : (
        <Card withBorder padding="lg">
          <WidgetMessage>{t("home:noWidgets")}</WidgetMessage>
        </Card>
      )}
    </div>
  );
}
