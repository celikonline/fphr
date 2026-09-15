import type { ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { CalendarHeart, CalendarRange, Cake, Palmtree, UserCircle } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { usePermission } from "@/components/permission-guard";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { getUpcomingBirthdays, getUpcomingHolidays } from "@/services/home.service";
import { getApiErrorStatus } from "@/lib/api-error";
import { daysUntil, formatDateOnly } from "@/lib/dates";

const HOLIDAY_DAYS = 90;
const BIRTHDAY_DAYS = 30;

/** 403/404 are expected (permission, access setting or module not deployed) - don't retry them. */
function retryUnlessClientError(failureCount: number, error: unknown): boolean {
  const status = getApiErrorStatus(error);
  if (status === 401 || status === 403 || status === 404) return false;
  return failureCount < 1;
}

function WidgetCard({ title, icon, badge, children }: { title: string; icon: ReactNode; badge?: ReactNode; children: ReactNode }) {
  return (
    <Card className="flex flex-col">
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          {icon}
          {title}
        </CardTitle>
        {badge}
      </CardHeader>
      <CardContent className="flex-1">{children}</CardContent>
    </Card>
  );
}

function WidgetMessage({ children }: { children: ReactNode }) {
  return <p className="py-4 text-center text-sm text-muted-foreground">{children}</p>;
}

function WidgetSkeleton() {
  return (
    <div className="space-y-2">
      <Skeleton className="h-6 w-full" />
      <Skeleton className="h-6 w-full" />
      <Skeleton className="h-6 w-3/4" />
    </div>
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
    <WidgetCard title={t("home:holidays.title")} icon={<CalendarHeart className="h-5 w-5 text-primary" />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:holidays.empty", { days: HOLIDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:holidays.empty", { days: HOLIDAY_DAYS })}</WidgetMessage>
      ) : (
        <ul className="divide-y divide-border">
          {data.map((holiday) => {
            const inDays = daysUntil(holiday.date);
            return (
              <li key={`${holiday.holidayCalendarId}-${holiday.date}-${holiday.name}`} className="flex items-center justify-between gap-3 py-2">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium">{holiday.name}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatDateOnly(holiday.date, i18n.language, { dateStyle: "full" })}
                  </p>
                </div>
                <Badge variant={inDays <= 0 ? "success" : "secondary"} className="shrink-0">
                  {inDays <= 0 ? t("home:today") : t("home:inDays", { count: inDays })}
                </Badge>
              </li>
            );
          })}
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
    <WidgetCard title={t("home:birthdays.title")} icon={<Cake className="h-5 w-5 text-primary" />}>
      {isLoading ? (
        <WidgetSkeleton />
      ) : error ? (
        <WidgetMessage>
          {getApiErrorStatus(error) === 404 ? t("home:birthdays.empty", { days: BIRTHDAY_DAYS }) : t("home:unavailable")}
        </WidgetMessage>
      ) : !data || data.length === 0 ? (
        <WidgetMessage>{t("home:birthdays.empty", { days: BIRTHDAY_DAYS })}</WidgetMessage>
      ) : (
        <ul className="divide-y divide-border">
          {data.map((birthday) => {
            const inDays = daysUntil(birthday.nextBirthday);
            return (
              <li key={birthday.id} className="flex items-center justify-between gap-3 py-2">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium">{birthday.displayName}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatDateOnly(birthday.nextBirthday, i18n.language, { day: "numeric", month: "long" })}
                  </p>
                </div>
                <Badge variant={inDays <= 0 ? "success" : "secondary"} className="shrink-0">
                  {inDays <= 0 ? t("home:today") : t("home:inDays", { count: inDays })}
                </Badge>
              </li>
            );
          })}
        </ul>
      )}
    </WidgetCard>
  );
}

function PlannedWidget({ title, icon, phase, description }: { title: string; icon: ReactNode; phase: number; description: string }) {
  const { t } = useTranslation(["navigation"]);
  return (
    <WidgetCard title={title} icon={icon} badge={<Badge variant="outline">{t("navigation:phase", { phase })}</Badge>}>
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
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">{t("home:greeting", { name: user?.firstName || user?.name || "" })}</h1>
          <p className="text-muted-foreground">{t("home:subtitle")}</p>
        </div>
        <Button asChild variant="outline">
          <Link to="/app/account">
            <UserCircle className="mr-2 h-4 w-4" /> {t("navigation:nav.account")}
          </Link>
        </Button>
      </div>

      {hasAnyWidget ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {canViewHolidays && <HolidaysWidget />}
          {showBirthdays && canViewDirectory && <BirthdaysWidget />}
          {showLeaves && (
            <PlannedWidget
              title={t("home:leaves.title")}
              icon={<Palmtree className="h-5 w-5 text-primary" />}
              phase={1}
              description={t("home:leaves.planned")}
            />
          )}
          {showEvents && (
            <PlannedWidget
              title={t("home:events.title")}
              icon={<CalendarRange className="h-5 w-5 text-primary" />}
              phase={4}
              description={t("home:events.planned")}
            />
          )}
        </div>
      ) : (
        <Card>
          <CardContent className="p-6">
            <WidgetMessage>{t("home:noWidgets")}</WidgetMessage>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
