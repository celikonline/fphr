import { Suspense, lazy, useMemo, useState, type CSSProperties, type ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Anchor, Avatar, Badge, Box, Card, ColorSwatch, Divider, Group, Menu, Progress, Skeleton, Stack, Text } from "@mantine/core";
import { BarChart3, Cake, CalendarDays, Check, ChevronRight, Inbox as InboxIcon, MoreVertical, Plane, RotateCw, Umbrella, Users, X } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { useModuleEnabled } from "@/hooks/use-module-enabled";
import { avatarColor, initials } from "@/components/employees/employee-badges";
import { useDays, useLeaveTypeNames } from "@/components/leave/leave-shared";
import { getDepartmentDistribution, getMyEmployee } from "@/services/employees.service";
import { getUpcomingBirthdays, getUpcomingHolidays, type UpcomingHoliday } from "@/services/home.service";
import { getLeaveCalendar, getMyLeaveBalances, getMyLeaveRequests, getTeamLeaveRequests, type LeaveRequestStatus } from "@/services/leave.service";
import { listInbox } from "@/services/workflow.service";
import { getApiErrorStatus } from "@/lib/api-error";
import { daysUntil, formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";

// The charting library is the single heaviest dependency on this screen and only one widget needs it,
// so it loads after the dashboard paints instead of holding up the first screen after sign-in.
const DonutChart = lazy(() => import("@mantine/charts").then((m) => ({ default: m.DonutChart })));

const HOLIDAY_DAYS = 90;
const BIRTHDAY_DAYS = 30;
const UPCOMING_LEAVE_DAYS = 30;
const LIST_LIMIT = 3;
const BIRTHDAY_LIMIT = 5;
const REQUEST_LIMIT = 4;
const APPROVAL_LIMIT = 5;
const SLATE = "var(--mantine-color-gray-6)";
const ACCENT = "var(--mantine-primary-color-filled)";
/** Categorical palette of the distribution chart, taken from the corporate colors. */
const CHART_COLORS = [
  "var(--mantine-color-navy-7)",
  "var(--mantine-color-brand-6)",
  "var(--mantine-color-orange-6)",
  "var(--mantine-color-teal-6)",
  "var(--mantine-color-red-6)",
  "var(--mantine-color-cyan-4)",
  "var(--mantine-color-yellow-6)",
  "var(--mantine-color-blue-6)",
  "var(--mantine-color-gold-6)",
];
/** Thin border plus a soft outer ring: the reference cards read as a double border. */
const CARD_STYLE: CSSProperties = {
  borderColor: "var(--mantine-color-default-border)",
  boxShadow: "0 0 0 4px var(--mantine-color-gray-light)",
};
const SHORT_DATE: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", year: "numeric" };
/** `InboxTaskDto.submittedAt` is a full timestamp (not a `DateOnly`), so `formatDateOnly` doesn't apply. */
const SUBMITTED_DATE: Intl.DateTimeFormatOptions = { day: "2-digit", month: "2-digit", year: "numeric" };

/** 403/404 are expected (permission, access setting or module not deployed) - don't retry them. */
function retryUnlessClientError(failureCount: number, error: unknown): boolean {
  const status = getApiErrorStatus(error);
  if (status === 401 || status === 403 || status === 404) return false;
  return failureCount < 1;
}

/** Colleague rows only carry a display name: first letter of its first and last word. */
function nameInitials(displayName: string): string {
  const parts = displayName.trim().split(/\s+/);
  return `${parts[0]?.charAt(0) ?? ""}${parts.length > 1 ? parts[parts.length - 1].charAt(0) : ""}`.toLocaleUpperCase("tr") || "?";
}

function WidgetFrame({ children }: { children: ReactNode }) {
  return (
    <Card withBorder radius="md" padding="lg" style={CARD_STYLE}>
      {children}
    </Card>
  );
}

function WidgetCard({
  title,
  icon,
  accent = SLATE,
  action,
  children,
}: {
  title: string;
  icon: ReactNode;
  accent?: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <WidgetFrame>
      <Group justify="space-between" gap="sm" wrap="nowrap" mb="xl">
        <Group gap="md" wrap="nowrap" className="min-w-0">
          <span
            className="flex h-8 w-8 shrink-0 items-center justify-center"
            style={{ backgroundColor: accent, color: "white", borderRadius: "var(--mantine-radius-md)" }}
          >
            {icon}
          </span>
          <Text fz={17} fw={450} truncate>
            {title}
          </Text>
        </Group>
        {action}
      </Group>
      {children}
    </WidgetFrame>
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

/** Kebab menu with real navigation targets (the reference shows ⋮ on most widgets and on each leave request row). */
function LinkMenu({ items }: { items: { to: string; label: string }[] }) {
  const { t } = useTranslation(["common"]);
  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target>
        <ActionIcon variant="subtle" size="sm" aria-label={t("common:actions")}>
          <MoreVertical size={18} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        {items.map((item) => (
          <Menu.Item key={item.to} component={Link} to={item.to}>
            {item.label}
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}

function FooterLink({ to, label }: { to: string; label: string }) {
  return (
    <Group justify="flex-end" mt="md">
      <Anchor component={Link} to={to} size="sm" className="inline-flex items-center gap-1">
        {label}
        <ChevronRight size={16} />
      </Anchor>
    </Group>
  );
}

/** Divided rows; `indent` starts the divider after a leading icon column, as in the reference. The first row sits closer to the header. */
function RowList<T>({
  items,
  rowKey,
  indent = 0,
  render,
}: {
  items: T[];
  rowKey: (item: T) => string;
  indent?: number;
  render: (item: T) => ReactNode;
}) {
  return (
    <ul>
      {items.map((item, index) => (
        <li key={rowKey(item)}>
          {index > 0 && <Divider ml={indent} />}
          <Group justify="space-between" gap="sm" wrap="nowrap" pt={index === 0 ? 2 : 10} pb={10}>
            {render(item)}
          </Group>
        </li>
      ))}
    </ul>
  );
}

function PersonName({ name, employeeId, canOpen }: { name: string; employeeId: string; canOpen: boolean }) {
  return canOpen ? (
    <Anchor component={Link} to={`/app/employees/${employeeId}`} size="sm" truncate>
      {name}
    </Anchor>
  ) : (
    <Text size="sm" truncate>
      {name}
    </Text>
  );
}

function Amount({ days, date }: { days?: string; date: string }) {
  return (
    <Group gap="sm" wrap="nowrap" className="shrink-0">
      {days && (
        <Text size="sm" c="dimmed">
          {days}
        </Text>
      )}
      <Text size="sm">{date}</Text>
    </Group>
  );
}

/** Own employee card ("Home.ShowOrgInfo"): headerless, name + ünvan/departman (resolved by the API from the org placement), manager, hire date. */
function OrgInfoWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const canReadEmployees = usePermission("employee.employees.read");
  const { data: employee, isLoading, error } = useQuery({
    queryKey: ["home", "org-info"],
    queryFn: getMyEmployee,
    retry: retryUnlessClientError,
  });
  const title = employee?.titleName ?? undefined;
  const department = employee?.departmentName ?? undefined;

  const row = (label: string, value: ReactNode) => (
    <>
      <Group justify="space-between" gap="sm" wrap="nowrap" py={12}>
        <Text size="sm" c="dimmed">
          {label}
        </Text>
        {value}
      </Group>
      <Divider />
    </>
  );

  return (
    <WidgetFrame>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error || !employee ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : (
        <>
          <Group justify="space-between" gap="md" wrap="nowrap" align="flex-start" mb={36}>
            <div className="min-w-0 pt-1">
              <Text fz={20} fw={500} truncate>
                {employee.displayName}
              </Text>
              {(title ?? department) && (
                <Text size="sm" mt="md" truncate>
                  {title ?? department}
                </Text>
              )}
              {title && department && (
                <Text size="sm" c="dimmed" truncate>
                  {department}
                </Text>
              )}
            </div>
            <Avatar size={88} radius="md" fz={28} color={avatarColor(employee.id)} styles={{ placeholder: { fontWeight: 500 } }}>
              {initials(employee.firstName, employee.lastName)}
            </Avatar>
          </Group>
          {row(
            t("home:orgInfo.manager"),
            employee.managerEmployeeId && employee.managerDisplayName ? (
              <PersonName name={employee.managerDisplayName} employeeId={employee.managerEmployeeId} canOpen={canReadEmployees} />
            ) : (
              <Text size="sm">—</Text>
            ),
          )}
          {row(
            t("home:orgInfo.hireDate"),
            <Text size="sm">{employee.hireDate ? formatDateOnly(employee.hireDate, i18n.language, SHORT_DATE) : "—"}</Text>,
          )}
          <FooterLink to="/app/account" label={t("home:orgInfo.account")} />
        </>
      )}
    </WidgetFrame>
  );
}

const STATUS_ICON: Record<LeaveRequestStatus, { background: string; color: string; icon: "check" | "x" }> = {
  pending: { background: "var(--mantine-color-orange-1)", color: "var(--mantine-color-orange-6)", icon: "check" },
  approved: { background: "#1e8e3e", color: "#ffffff", icon: "check" },
  rejected: { background: "var(--mantine-color-red-4)", color: "#ffffff", icon: "x" },
  withdrawn: { background: "var(--mantine-color-gray-2)", color: "var(--mantine-color-gray-6)", icon: "x" },
  cancelled: { background: "var(--mantine-color-gray-2)", color: "var(--mantine-color-gray-6)", icon: "x" },
};

function LeaveStatusIcon({ status }: { status: LeaveRequestStatus }) {
  const { t } = useTranslation(["leave"]);
  const look = STATUS_ICON[status];
  const Icon = look.icon === "check" ? Check : X;
  return (
    <span
      className="flex h-6 w-6 shrink-0 items-center justify-center"
      style={{ backgroundColor: look.background, color: look.color, borderRadius: "50%" }}
      title={t(`leave:status.${status}`)}
    >
      <Icon size={14} strokeWidth={3} />
    </span>
  );
}

function LegendDot({ color, label, value }: { color: string; label: string; value: string }) {
  return (
    <Group gap={8} wrap="nowrap">
      <ColorSwatch color={color} size={8} withShadow={false} />
      <Text size="sm">
        {label}{" "}
        <Text span inherit fw={500}>
          {value}
        </Text>
      </Text>
    </Group>
  );
}

/** "İzin Bilgilerim": the annual (else first) balance with an available/future/used bar, plus the latest own requests. */
function LeaveInfoWidget() {
  const { t, i18n } = useTranslation(["home", "leave", "navigation"]);
  const days = useDays();
  const balancesQuery = useQuery({
    queryKey: ["home", "leave-balances"],
    queryFn: getMyLeaveBalances,
    retry: retryUnlessClientError,
  });
  // Own requests need their own permission; without it (or on error) the request list is hidden.
  const canViewRequests = usePermission("leave.my.requests");
  const requestsQuery = useQuery({
    queryKey: ["home", "leave-requests"],
    queryFn: getMyLeaveRequests,
    enabled: canViewRequests,
    retry: retryUnlessClientError,
  });
  const balances = balancesQuery.data;
  const typeName = useLeaveTypeNames(balances, undefined);
  const recentRequests = useMemo(
    () => [...(requestsQuery.data ?? [])].sort((a, b) => b.startDate.localeCompare(a.startDate)).slice(0, REQUEST_LIMIT),
    [requestsQuery.data],
  );
  const primary = balances?.balances.find((item) => item.leaveTypeCode === "annual") ?? balances?.balances[0];

  const refresh = () => {
    void balancesQuery.refetch();
    if (canViewRequests) void requestsQuery.refetch();
  };

  let balance: ReactNode = null;
  if (primary) {
    const b = primary.balance;
    const available = Math.max(0, b.availableBalance);
    const pool = available + b.futureApprovedInPeriod + b.totalUsed;
    const pct = (value: number) => (pool <= 0 ? 0 : Math.max(0, Math.min(100, (value / pool) * 100)));
    balance = (
      <Stack gap={0}>
        <Text size="sm" c="dimmed">
          {t("home:leaveInfo.availableUntil", { date: formatDateOnly(b.currentPeriodTo, i18n.language, SHORT_DATE) })}
        </Text>
        <Text fz={28} fw={500} c="green.8" mt="sm" lh={1.3}>
          {days(b.availableBalance)} {t("leave:days")}
        </Text>
        <Progress.Root size={6} mt="sm" radius="xl">
          <Progress.Section value={pct(available)} color="green.7" />
          <Progress.Section value={pct(b.futureApprovedInPeriod)} color="orange.5" />
          <Progress.Section value={pct(b.totalUsed)} color="red.6" />
        </Progress.Root>
        <Group justify="space-between" mt="sm">
          <LegendDot color="var(--mantine-color-orange-5)" label={t("home:leaveInfo.future")} value={days(b.futureApprovedInPeriod)} />
          <LegendDot color="var(--mantine-color-red-6)" label={t("home:leaveInfo.used")} value={days(b.totalUsed)} />
        </Group>
      </Stack>
    );
  }

  return (
    <WidgetCard
      title={t("home:leaveInfo.title")}
      icon={<Plane size={18} />}
      accent={ACCENT}
      action={
        <ActionIcon
          variant="subtle"
          size="sm"
          onClick={refresh}
          loading={balancesQuery.isFetching || requestsQuery.isFetching}
          aria-label={t("home:refresh")}
        >
          <RotateCw size={18} />
        </ActionIcon>
      }
    >
      {balancesQuery.isLoading ? (
        <WidgetSkeleton />
      ) : balancesQuery.error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : !primary ? (
        <WidgetMessage>{t("home:leaveInfo.empty")}</WidgetMessage>
      ) : (
        <>
          {balance}
          {canViewRequests && !requestsQuery.error && recentRequests.length > 0 && (
            <Box mt={40}>
              <RowList
                items={recentRequests}
                rowKey={(request) => request.id}
                indent={40}
                render={(request) => (
                  <>
                    <Group gap="md" wrap="nowrap" className="min-w-0">
                      <LeaveStatusIcon status={request.status} />
                      <Text size="sm" truncate>
                        {typeName(request)}
                      </Text>
                    </Group>
                    <Group gap="xs" wrap="nowrap" className="shrink-0">
                      <Amount
                        days={`${days(request.calculatedDays)}${t("home:daysSuffix")}`}
                        date={formatDateOnly(request.startDate, i18n.language, SHORT_DATE)}
                      />
                      <LinkMenu items={[{ to: "/app/leave", label: t("navigation:nav.leaves") }]} />
                    </Group>
                  </>
                )}
              />
            </Box>
          )}
          <Divider mt="md" />
          <FooterLink to="/app/leave" label={t("home:viewAll")} />
        </>
      )}
    </WidgetCard>
  );
}

/** Colleagues' upcoming/pending leave in the next `UPCOMING_LEAVE_DAYS` days. */
function UpcomingLeavesWidget() {
  const { t, i18n } = useTranslation(["home", "navigation"]);
  const days = useDays();
  const canReadEmployees = usePermission("employee.employees.read");
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
  const entries = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, LIST_LIMIT),
    [data],
  );

  return (
    <WidgetCard
      title={t("home:leaves.title")}
      icon={<Plane size={18} />}
      action={<LinkMenu items={[{ to: "/app/leave/calendar", label: t("navigation:nav.calendar") }]} />}
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error && getApiErrorStatus(error) !== 404 ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : entries.length === 0 ? (
        <WidgetMessage>{t("home:leaves.empty", { days: UPCOMING_LEAVE_DAYS })}</WidgetMessage>
      ) : (
        <RowList
          items={entries}
          rowKey={(entry) => entry.requestId}
          render={(entry) => (
            <>
              <Group gap="sm" wrap="nowrap" className="min-w-0">
                <Avatar size={30} radius="xl" color={avatarColor(entry.employeeId)}>
                  {nameInitials(entry.employeeName)}
                </Avatar>
                <PersonName name={entry.employeeName} employeeId={entry.employeeId} canOpen={canReadEmployees} />
              </Group>
              <Amount
                days={`${days(entry.calculatedDays)}${t("home:daysSuffix")}`}
                date={daysUntil(entry.startDate) <= 0 ? t("home:today") : formatDateOnly(entry.startDate, i18n.language, SHORT_DATE)}
              />
            </>
          )}
        />
      )}
    </WidgetCard>
  );
}

/** "İzin Talepleri": pending leave requests of the caller's team, awaiting review. */
function TeamLeaveRequestsWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const days = useDays();
  const canReadEmployees = usePermission("employee.employees.read");
  const { data, isLoading, error, isFetching, refetch } = useQuery({
    queryKey: ["home", "team-leave-requests"],
    queryFn: getTeamLeaveRequests,
    retry: retryUnlessClientError,
  });
  const entries = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, LIST_LIMIT),
    [data],
  );

  return (
    <WidgetCard
      title={t("home:teamRequests.title")}
      icon={<Plane size={18} />}
      accent="var(--mantine-color-orange-6)"
      action={
        <ActionIcon variant="subtle" size="sm" onClick={() => void refetch()} loading={isFetching} aria-label={t("home:refresh")}>
          <RotateCw size={18} />
        </ActionIcon>
      }
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error && getApiErrorStatus(error) !== 404 ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : entries.length === 0 ? (
        <WidgetMessage>{t("home:teamRequests.empty")}</WidgetMessage>
      ) : (
        <>
          <RowList
            items={entries}
            rowKey={(entry) => entry.requestId}
            render={(entry) => (
              <>
                <Group gap="sm" wrap="nowrap" className="min-w-0">
                  <Avatar size={30} radius="xl" color={avatarColor(entry.employeeId)}>
                    {nameInitials(entry.employeeName)}
                  </Avatar>
                  <PersonName name={entry.employeeName} employeeId={entry.employeeId} canOpen={canReadEmployees} />
                </Group>
                <Amount
                  days={`${days(entry.calculatedDays)}${t("home:daysSuffix")}`}
                  date={formatDateOnly(entry.startDate, i18n.language, SHORT_DATE)}
                />
              </>
            )}
          />
          <Divider mt="md" />
          <FooterLink to="/app/leave/calendar" label={t("home:teamRequests.viewCalendar")} />
        </>
      )}
    </WidgetCard>
  );
}

/**
 * "Onay Bekleyenler": a read-only summary of the caller's approval inbox (Faz 1 tur 1). Decisions stay in
 * `/app/inbox` - this widget only surfaces the count and the top items. `InboxTaskDto.submittedAt` (Faz 1
 * tur 2, BE-added) shows a short TR date when present; older/unfilled tasks just omit it.
 */
function PendingApprovalsWidget() {
  const { t, i18n } = useTranslation(["home", "workflow"]);
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "pending-approvals"],
    queryFn: listInbox,
    retry: retryUnlessClientError,
  });
  const entries = useMemo(() => (data ?? []).slice(0, APPROVAL_LIMIT), [data]);

  // Not an approver, or an approver with nothing pending right now - the card simply doesn't render.
  if (!isLoading && !error && entries.length === 0) return null;

  return (
    <WidgetCard
      title={t("home:pendingApprovals.title")}
      icon={<InboxIcon size={18} />}
      accent="var(--mantine-color-blue-6)"
      action={
        data && data.length > 0 ? (
          <Badge variant="light" color="blue">
            {data.length}
          </Badge>
        ) : undefined
      }
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : (
        <>
          <RowList
            items={entries}
            rowKey={(entry) => entry.taskId}
            render={(entry) => (
              <>
                <Text size="sm" truncate>
                  {entry.requesterName ?? "—"}
                </Text>
                <Group gap="xs" wrap="nowrap" className="shrink-0">
                  {entry.submittedAt && (
                    <Text size="sm" c="dimmed">
                      {new Intl.DateTimeFormat(i18n.language, SUBMITTED_DATE).format(new Date(entry.submittedAt))}
                    </Text>
                  )}
                  <Text size="sm" c="dimmed" truncate>
                    {t(`workflow:requestType.${entry.requestType}`)}
                  </Text>
                </Group>
              </>
            )}
          />
          <Divider mt="md" />
          <FooterLink to="/app/inbox" label={t("home:pendingApprovals.viewAll")} />
        </>
      )}
    </WidgetCard>
  );
}

interface HolidayGroup {
  key: string;
  name: string;
  start: string;
  days: number;
}

/** One row per holiday: consecutive dates with the same name (a multi-day bayram) collapse into a day count. */
function groupHolidays(holidays: UpcomingHoliday[]): HolidayGroup[] {
  const seen = new Set<string>();
  const groups: HolidayGroup[] = [];
  for (const holiday of [...holidays].sort((a, b) => a.date.localeCompare(b.date))) {
    const id = `${holiday.name}|${holiday.date}`;
    if (seen.has(id)) continue;
    seen.add(id);
    const last = groups[groups.length - 1];
    if (last && last.name === holiday.name) {
      const next = parseDateOnly(last.start);
      next.setDate(next.getDate() + last.days);
      if (toDateOnly(next) === holiday.date) {
        last.days += 1;
        continue;
      }
    }
    groups.push({ key: id, name: holiday.name, start: holiday.date, days: 1 });
  }
  return groups;
}

function HolidaysWidget() {
  const { t, i18n } = useTranslation(["home"]);
  const days = useDays();
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-holidays", HOLIDAY_DAYS],
    queryFn: () => getUpcomingHolidays(HOLIDAY_DAYS),
    retry: retryUnlessClientError,
  });
  const groups = useMemo(() => groupHolidays(data ?? []).slice(0, LIST_LIMIT), [data]);

  return (
    <WidgetCard title={t("home:holidays.title")} icon={<Umbrella size={18} />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error && getApiErrorStatus(error) !== 404 ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : groups.length === 0 ? (
        <WidgetMessage>{t("home:holidays.empty", { days: HOLIDAY_DAYS })}</WidgetMessage>
      ) : (
        <RowList
          items={groups}
          rowKey={(group) => group.key}
          render={(group) => (
            <>
              <Text size="sm" truncate>
                {group.name}
              </Text>
              <Amount days={`${days(group.days)}${t("home:daysSuffix")}`} date={formatDateOnly(group.start, i18n.language, SHORT_DATE)} />
            </>
          )}
        />
      )}
    </WidgetCard>
  );
}

function BirthdaysWidget() {
  const { t, i18n } = useTranslation(["home", "navigation"]);
  const canReadEmployees = usePermission("employee.employees.read");
  const { data, isLoading, error } = useQuery({
    queryKey: ["home", "upcoming-birthdays", BIRTHDAY_DAYS],
    queryFn: () => getUpcomingBirthdays(BIRTHDAY_DAYS),
    retry: retryUnlessClientError,
  });
  const items = (data ?? []).slice(0, BIRTHDAY_LIMIT);

  return (
    <WidgetCard
      title={t("home:birthdays.title")}
      icon={<Cake size={18} />}
      action={<LinkMenu items={[{ to: "/app/employees", label: t("navigation:nav.employees") }]} />}
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error && getApiErrorStatus(error) !== 404 ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : items.length === 0 ? (
        <WidgetMessage>{t("home:birthdays.empty", { days: BIRTHDAY_DAYS })}</WidgetMessage>
      ) : (
        <RowList
          items={items}
          rowKey={(birthday) => birthday.id}
          render={(birthday) => (
            <>
              <Group gap="sm" wrap="nowrap" className="min-w-0">
                <Avatar size={30} radius="xl" color={avatarColor(birthday.id)}>
                  {nameInitials(birthday.displayName)}
                </Avatar>
                <PersonName name={birthday.displayName} employeeId={birthday.id} canOpen={canReadEmployees} />
              </Group>
              <Amount date={formatDateOnly(birthday.nextBirthday, i18n.language, { day: "numeric", month: "short" })} />
            </>
          )}
        />
      )}
    </WidgetCard>
  );
}

/** Active headcount per department; the donut's center and the footer both show the grand total. */
function DepartmentDistributionWidget() {
  const { t } = useTranslation(["home", "navigation"]);
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
        color: CHART_COLORS[index % CHART_COLORS.length],
      })),
    [data, t],
  );
  const total = chartData.reduce((sum, item) => sum + item.value, 0);

  return (
    <WidgetCard
      title={t("home:departmentDistribution.title")}
      icon={<BarChart3 size={18} />}
      action={<LinkMenu items={[{ to: "/app/employees", label: t("navigation:nav.employees") }]} />}
    >
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>{t("home:unavailable")}</WidgetMessage>
      ) : total === 0 ? (
        <WidgetMessage>{t("home:departmentDistribution.empty")}</WidgetMessage>
      ) : (
        <>
          <Group justify="center" py="md">
            <Box pos="relative" w={230} h={230}>
              <Suspense fallback={<Skeleton circle height={230} />}>
                <DonutChart data={chartData} size={230} thickness={48} startAngle={90} endAngle={-270} withTooltip={false} strokeWidth={0} />
              </Suspense>
              <Stack gap={0} align="center" justify="center" pos="absolute" top={0} left={0} right={0} bottom={0}>
                <Text size="sm" c="dimmed" truncate maw={120} ta="center">
                  {t("home:departmentDistribution.total")}
                </Text>
                <Text data-testid="department-distribution-center-total" fz={30} fw={400} lh={1.2}>
                  {total}
                </Text>
              </Stack>
            </Box>
          </Group>
          <Divider mt="md" />
          <Group gap="xs" pt="md">
            <Users size={18} color="var(--mantine-color-dimmed)" />
            <Text size="sm">{total}</Text>
          </Group>
        </>
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
      action={
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
  const canViewHolidays = usePermission("organization.holidays.view");
  const canViewDirectory = usePermission("employee.directory.view");
  // senseik-057: widgets of a module the tenant turned off (Ayarlar → Modüller) are hidden, never 403-ing.
  const leaveEnabled = useModuleEnabled("leave");
  const announcementsEnabled = useModuleEnabled("announcements");
  const canViewLeaveBalances = usePermission("leave.my.balances") && leaveEnabled;
  const canViewTeamRequests = usePermission("leave.requests.read") && leaveEnabled;
  const canViewInbox = usePermission("workflow.inbox.view");
  // The leave calendar endpoint also requires the Calendar.ShowMenu access setting.
  const hasLeaveCalendarPermission = usePermission("leave.my.calendar");
  const showCalendarMenu = useAccessSetting("showCalendarMenu");
  const canViewLeaveCalendar = hasLeaveCalendarPermission && showCalendarMenu && leaveEnabled;
  const showOrgInfo = useAccessSetting("showOrgInfo");
  const showBirthdays = useAccessSetting("showUpcomingBirthdays");
  const showLeaves = useAccessSetting("showUpcomingLeaves");
  const showEvents = useAccessSetting("showEventCalendar") && announcementsEnabled;

  // The reference dashboard's three columns; a hidden widget just leaves its column shorter.
  const columns: ReactNode[][] = [
    [showOrgInfo && <OrgInfoWidget key="org" />, canViewDirectory && <DepartmentDistributionWidget key="distribution" />],
    [canViewLeaveBalances && <LeaveInfoWidget key="leave" />, showBirthdays && canViewDirectory && <BirthdaysWidget key="birthdays" />],
    [
      canViewInbox && <PendingApprovalsWidget key="pending-approvals" />,
      canViewTeamRequests && <TeamLeaveRequestsWidget key="team-requests" />,
      showLeaves && canViewLeaveCalendar && <UpcomingLeavesWidget key="upcoming" />,
      canViewHolidays && <HolidaysWidget key="holidays" />,
      showEvents && (
        <PlannedWidget
          key="events"
          title={t("home:events.title")}
          icon={<CalendarDays size={18} />}
          phase={4}
          description={t("home:events.planned")}
        />
      ),
    ],
  ].map((column) => column.filter(Boolean));
  const hasAnyWidget = columns.some((column) => column.length > 0);

  return (
    <div className="mx-auto max-w-6xl p-5 md:p-7">
      {hasAnyWidget ? (
        <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-3">
          {columns.map((column, index) => (
            <div key={index} className="flex flex-col gap-5">
              {column}
            </div>
          ))}
        </div>
      ) : (
        <WidgetFrame>
          <WidgetMessage>{t("home:noWidgets")}</WidgetMessage>
        </WidgetFrame>
      )}
    </div>
  );
}
