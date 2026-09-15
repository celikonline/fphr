import { useMemo, type ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Avatar, Badge, Button, Card, Divider, Group, Progress, SimpleGrid, Skeleton, Stack, Text, Title } from "@mantine/core";
import { DonutChart } from "@mantine/charts";
import { Building2, CalendarCheck, CalendarHeart, CalendarRange, Cake, IdCard, Palmtree, UserCircle } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { initials } from "@/components/employees/employee-badges";
import { useOrgLevels, useOrgUnits } from "@/components/employees/use-org-units";
import { Field, LeaveStatusBadge, useDays, useLeaveName, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { getDepartmentDistribution, getMyCareer, getMyEmployee } from "@/services/employees.service";
import { getUpcomingBirthdays, getUpcomingHolidays } from "@/services/home.service";
import { getLeaveCalendar, getMyLeaveBalances, getMyLeaveRequests } from "@/services/leave.service";
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

function WidgetCard({ title, icon, badge, children }: { title: string; icon: ReactNode; badge?: ReactNode; children: ReactNode }) {
  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="sm" mb="sm" wrap="nowrap">
        <Title order={5} className="flex items-center gap-2">
          {icon}
          {title}
        </Title>
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

function DayBadge({ inDays }: { inDays: number }) {
  const { t } = useTranslation(["home"]);
  return (
    <Badge variant="light" color={inDays <= 0 ? "green" : "gray"} className="shrink-0">
      {inDays <= 0 ? t("home:today") : t("home:inDays", { count: inDays })}
    </Badge>
  );
}

function WidgetRow({ title, subtitle, inDays, divided }: { title: string; subtitle: string; inDays: number; divided: boolean }) {
  return (
    <li>
      {divided && <Divider />}
      <Group justify="space-between" gap="sm" wrap="nowrap" py="xs">
        <div className="min-w-0">
          <Text size="sm" fw={500} truncate>
            {title}
          </Text>
          <Text size="xs" c="dimmed">
            {subtitle}
          </Text>
        </div>
        <DayBadge inDays={inDays} />
      </Group>
    </li>
  );
}

function WidgetList({ children }: { children: ReactNode }) {
  return <ul>{children}</ul>;
}

function HolidaysWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-holidays", HOLIDAY_DAYS],
    queryFn: () => getUpcomingHolidays(HOLIDAY_DAYS),
    retry: retryUnlessClientError,
  });

  return (
    <WidgetCard title={t("home:holidays.title")} icon={<CalendarHeart size={20} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:holidays.empty", { days: HOLIDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:holidays.empty", { days: HOLIDAY_DAYS })}</WidgetMessage>
      ) : (
        <WidgetList>
          {data.map((holiday, index) => (
            <WidgetRow
              key={`${holiday.holidayCalendarId}-${holiday.date}-${holiday.name}`}
              title={holiday.name}
              subtitle={formatDateOnly(holiday.date, i18n.language, { dateStyle: "full" })}
              inDays={daysUntil(holiday.date)}
              divided={index > 0}
            />
          ))}
        </WidgetList>
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
    <WidgetCard title={t("home:birthdays.title")} icon={<Cake size={20} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:birthdays.empty", { days: BIRTHDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:birthdays.empty", { days: BIRTHDAY_DAYS })}</WidgetMessage>
      ) : (
        <WidgetList>
          {data.map((birthday, index) => (
            <WidgetRow
              key={birthday.id}
              title={birthday.displayName}
              subtitle={formatDateOnly(birthday.nextBirthday, i18n.language, { day: "numeric", month: "long" })}
              inDays={daysUntil(birthday.nextBirthday)}
              divided={index > 0}
            />
          ))}
        </WidgetList>
      )}
    </WidgetCard>
  );
}

/** Own employee card ("Home.ShowOrgInfo"): name, ünvan/departman (career assignments), manager, hire date. */
function OrgInfoWidget() {
  const { t, i18n } = useTranslation(["home"]);
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
    <WidgetCard title={t("home:orgInfo.title")} icon={<IdCard size={20} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error || !employee ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : (
        <Stack gap="md">
          <Group gap="md" wrap="nowrap">
            <Avatar size={56} radius="xl">
              {initials(employee.firstName, employee.lastName)}
            </Avatar>
            <div className="min-w-0">
              <Text fw={600} truncate>
                {employee.displayName}
              </Text>
              {(title || department) && (
                <Text size="sm" c="dimmed" truncate>
                  {[title, department].filter(Boolean).join(" · ")}
                </Text>
              )}
            </div>
          </Group>
          <dl className="grid gap-4 sm:grid-cols-2">
            <Field label={t("home:orgInfo.manager")}>{employee.managerDisplayName ?? "—"}</Field>
            <Field label={t("home:orgInfo.hireDate")}>
              {employee.hireDate ? formatDateOnly(employee.hireDate, i18n.language) : "—"}
            </Field>
          </dl>
        </Stack>
      )}
    </WidgetCard>
  );
}

/** Own leave balances (per type, with a used/entitled progress bar) plus the last 4 own requests. */
function LeaveInfoWidget() {
  const { t, i18n } = useTranslation(["home", "leave"]);
  const days = useDays();
  const leaveName = useLeaveName();
  const { data: balances, isLoading, error } = useQuery({
    queryKey: ["home", "leave-balances"],
    queryFn: getMyLeaveBalances,
    retry: retryUnlessClientError,
  });
  const { data: requests } = useQuery({
    queryKey: ["home", "leave-requests"],
    queryFn: getMyLeaveRequests,
  });
  const typeName = useLeaveTypeNames(balances, undefined);
  const recentRequests = useMemo(
    () => [...(requests ?? [])].sort((a, b) => b.startDate.localeCompare(a.startDate)).slice(0, 4),
    [requests],
  );

  return (
    <WidgetCard title={t("home:leaveInfo.title")} icon={<CalendarCheck size={20} color={ICON_COLOR} />}>
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
            const entitled = b.totalUsed + b.currentBalance;
            const usedPct = entitled <= 0 ? 0 : Math.min(100, (b.totalUsed / entitled) * 100);
            return (
              <Stack key={b.leaveTypeId} gap="xs">
                <Group justify="space-between" align="baseline" gap="xs">
                  <Text size="sm" c="dimmed">
                    {leaveName(item.leaveTypeName)}
                  </Text>
                  <Text size="xl" fw={700}>
                    {days(b.availableBalance)}{" "}
                    <Text span size="sm" fw={400} c="dimmed">
                      {t("leave:days")}
                    </Text>
                  </Text>
                </Group>
                <Text size="xs" c="dimmed">
                  {t("home:leaveInfo.availableUntil", { date: formatDateOnly(b.currentPeriodTo, i18n.language) })}
                </Text>
                <Progress
                  value={usedPct}
                  size="sm"
                  color={b.currentBalance < 0 ? "red" : undefined}
                  aria-label={`${t("home:leaveInfo.used")}: ${days(b.totalUsed)} / ${days(entitled)} ${t("leave:days")}`}
                />
                <Group gap="lg">
                  <Text size="xs" c="dimmed">
                    {t("home:leaveInfo.future")}: {days(b.futureApprovedInPeriod)}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {t("home:leaveInfo.used")}: {days(b.totalUsed)}
                  </Text>
                </Group>
              </Stack>
            );
          })}

          <Divider label={t("home:leaveInfo.recentTitle")} labelPosition="left" />
          {recentRequests.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t("leave:requests.empty")}
            </Text>
          ) : (
            <Stack gap={0}>
              {recentRequests.map((request, index) => (
                <div key={request.id}>
                  {index > 0 && <Divider />}
                  <Group justify="space-between" gap="sm" py="xs" wrap="nowrap">
                    <div className="min-w-0">
                      <Text size="sm" fw={500} truncate>
                        {typeName(request)}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {formatDateOnly(request.startDate, i18n.language)} – {formatDateOnly(request.endDate, i18n.language)} ·{" "}
                        {days(request.calculatedDays)} {t("leave:days")}
                      </Text>
                    </div>
                    <LeaveStatusBadge status={request.status} />
                  </Group>
                </div>
              ))}
            </Stack>
          )}
        </Stack>
      )}
    </WidgetCard>
  );
}

/** Colleagues' upcoming/pending leave in the next `UPCOMING_LEAVE_DAYS` days. */
function UpcomingLeavesWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const from = toDateOnly(new Date());
  const to = toDateOnly(new Date(Date.now() + UPCOMING_LEAVE_DAYS * 86_400_000));
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-leaves", from, to],
    queryFn: () => getLeaveCalendar({ from, to }),
    retry: retryUnlessClientError,
  });
  const entries = useMemo(() => [...(data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate)), [data]);

  return (
    <WidgetCard title={t("home:leaves.title")} icon={<Palmtree size={20} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:leaves.empty", { days: UPCOMING_LEAVE_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : entries.length === 0 ? (
        <WidgetMessage>{t("home:leaves.empty", { days: UPCOMING_LEAVE_DAYS })}</WidgetMessage>
      ) : (
        <WidgetList>
          {entries.slice(0, 6).map((entry, index) => (
            <WidgetRow
              key={entry.requestId}
              title={entry.employeeName}
              subtitle={`${formatDateOnly(entry.startDate, i18n.language)} – ${formatDateOnly(entry.endDate, i18n.language)}`}
              inDays={daysUntil(entry.startDate)}
              divided={index > 0}
            />
          ))}
        </WidgetList>
      )}
    </WidgetCard>
  );
}

/** Active headcount per department, from the new `department-distribution` endpoint. */
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
        name: item.departmentName ?? t("home:departmentDistribution.unassigned"),
        value: item.count,
        color: DEPARTMENT_COLORS[index % DEPARTMENT_COLORS.length],
      })),
    [data, t],
  );
  const total = chartData.reduce((sum, item) => sum + item.value, 0);

  return (
    <WidgetCard title={t("home:departmentDistribution.title")} icon={<Building2 size={20} color={ICON_COLOR} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : total === 0 ? (
        <WidgetMessage>{t("home:departmentDistribution.empty")}</WidgetMessage>
      ) : (
        <Stack align="center" gap="md">
          <DonutChart data={chartData} chartLabel={String(total)} size={160} thickness={22} />
          <SimpleGrid cols={2} spacing="xs" className="w-full">
            {chartData.map((item) => (
              <Group key={item.name} gap="xs" wrap="nowrap">
                <span
                  className="h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: `var(--mantine-color-${item.color}-6)` }}
                />
                <Text size="xs" c="dimmed" truncate>
                  {item.name}
                </Text>
                <Text size="xs" fw={600} className="ml-auto">
                  {item.value}
                </Text>
              </Group>
            ))}
          </SimpleGrid>
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
  const { t } = useTranslation(["home", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const canViewHolidays = usePermission("organization.holidays.view");
  const canViewDirectory = usePermission("employee.directory.view");
  const canViewLeaveBalances = usePermission("leave.my.balances");
  const canViewLeaveCalendar = usePermission("leave.my.calendar");
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

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={2}>{t("home:greeting", { name: user?.firstName || user?.name || "" })}</Title>
          <Text c="dimmed">{t("home:subtitle")}</Text>
        </div>
        <Button component={Link} to="/app/account" variant="default" leftSection={<UserCircle size={16} />}>
          {t("navigation:nav.account")}
        </Button>
      </Group>

      {hasAnyWidget ? (
        <SimpleGrid cols={{ base: 1, md: 2, lg: 3 }} spacing="md">
          {showOrgInfo && <OrgInfoWidget />}
          {canViewHolidays && <HolidaysWidget />}
          {showBirthdays && canViewDirectory && <BirthdaysWidget />}
          {canViewLeaveBalances && <LeaveInfoWidget />}
          {showLeaves && canViewLeaveCalendar && <UpcomingLeavesWidget />}
          {canViewDirectory && <DepartmentDistributionWidget />}
          {showEvents && (
            <PlannedWidget
              title={t("home:events.title")}
              icon={<CalendarRange size={20} color={ICON_COLOR} />}
              phase={4}
              description={t("home:events.planned")}
            />
          )}
        </SimpleGrid>
      ) : (
        <Card withBorder padding="lg">
          <WidgetMessage>{t("home:noWidgets")}</WidgetMessage>
        </Card>
      )}
    </div>
  );
}
