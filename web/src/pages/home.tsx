import type { ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Button, Card, Divider, Group, SimpleGrid, Skeleton, Stack, Text, Title } from "@mantine/core";
import { CalendarHeart, CalendarRange, Cake, Palmtree, UserCircle } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { getUpcomingBirthdays, getUpcomingHolidays } from "@/services/home.service";
import { getApiErrorStatus } from "@/lib/api-error";
import { daysUntil, formatDateOnly } from "@/lib/dates";

const HOLIDAY_DAYS = 90;
const BIRTHDAY_DAYS = 30;
const ICON_COLOR = "var(--mantine-primary-color-filled)";

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
  const showBirthdays = useAccessSetting("showUpcomingBirthdays");
  const showLeaves = useAccessSetting("showUpcomingLeaves");
  const showEvents = useAccessSetting("showEventCalendar");

  const hasAnyWidget = canViewHolidays || (showBirthdays && canViewDirectory) || showLeaves || showEvents;

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
        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
          {canViewHolidays && <HolidaysWidget />}
          {showBirthdays && canViewDirectory && <BirthdaysWidget />}
          {showLeaves && (
            <PlannedWidget
              title={t("home:leaves.title")}
              icon={<Palmtree size={20} color={ICON_COLOR} />}
              phase={1}
              description={t("home:leaves.planned")}
            />
          )}
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
