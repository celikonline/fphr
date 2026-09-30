import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  AppState,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import { useAuthStore } from "@/store/authStore";
import { BrandMark } from "@/components/auth/BrandMark";
import { LogoutConfirmSheet } from "@/components/auth/LogoutConfirmSheet";
import { Avatar, ErrorBanner, IconCircleButton, Screen, SectionHeader } from "@/components/ui";
import { leaveService, type LeaveBalanceItem } from "@/services/LeaveService";
import { approvalsService } from "@/services/ApprovalsService";
import { notificationsService } from "@/services/NotificationsService";
import {
  homeFeedService,
  selectAnnouncements,
  selectPeopleOffToday,
  selectUpcomingBirthdays,
  selectUpcomingEvents,
  sortBalancesAnnualFirst,
  type AnnouncementFeedItem,
  type CompanyCalendarEntry,
  type PersonOff,
  type UpcomingBirthday,
} from "@/services/HomeFeedService";
import { toLocalDateOnly } from "@/lib/formatters";
import { fontFor, palette, text } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { ActionGrid } from "./home/ActionGrid";
import { AnnouncementCard } from "./home/AnnouncementCard";
import { BalanceCarousel } from "./home/BalanceCarousel";
import { BirthdayCard } from "./home/BirthdayCard";
import { EventCard } from "./home/EventCard";
import { HomeMenu, type HomeMenuAction } from "./home/HomeMenu";
import { PeopleOffCard } from "./home/PeopleOffCard";
import { sectionFrom } from "./home/homeSections";

type DashboardNavigationProp = NativeStackNavigationProp<AppStackParamList>;

/** N1: badge caps at "9+" instead of growing the pill indefinitely. */
export function formatUnreadBadge(count: number): string {
  return count > 9 ? "9+" : String(count);
}

/** Spec §4: birthdays and company events are looked up this many days ahead. */
const LOOKAHEAD_DAYS = 30;

/** `null` = the section is hidden (request failed or nothing to show), see `sectionFrom`. */
type FeedSections = {
  peopleOff: PersonOff[] | null;
  birthdays: Array<UpcomingBirthday & { daysLeft: number }> | null;
  events: CompanyCalendarEntry[] | null;
  announcements: AnnouncementFeedItem[] | null;
};

const EMPTY_SECTIONS: FeedSections = {
  peopleOff: null,
  birthdays: null,
  events: null,
  announcements: null,
};

function daysFromToday(days: number): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + days);
}

export default function DashboardScreen() {
  const { t, i18n } = useTranslation("home");
  const { user } = useAuthStore();
  const navigation = useNavigation<DashboardNavigationProp>();
  const selfEmployeeId = user?.employeeId ?? undefined;

  const [balances, setBalances] = useState<LeaveBalanceItem[] | null>(null);
  const [balanceError, setBalanceError] = useState(false);
  const [approvalsCount, setApprovalsCount] = useState(0);
  const [sections, setSections] = useState<FeedSections>(EMPTY_SECTIONS);
  const [refreshing, setRefreshing] = useState(false);
  const [unreadCount, setUnreadCount] = useState(0);
  const [menuVisible, setMenuVisible] = useState(false);
  const [logoutConfirmVisible, setLogoutConfirmVisible] = useState(false);

  const loadUnreadCount = useCallback(async () => {
    try {
      setUnreadCount(await notificationsService.getUnreadCount());
    } catch {
      // Bell badge just stays at its last known value - not worth an error banner over.
    }
  }, []);

  // FG-008: the "Onaylarım" badge, refreshed the same way as the bell above - a toplu/single karar
  // given on `ApprovalsScreen` (pushed on top of this tab, still mounted underneath) only shows up
  // here once the user comes back to Home, which is exactly when this fires.
  const loadApprovalsCount = useCallback(async () => {
    try {
      setApprovalsCount((await approvalsService.getInbox()).length);
    } catch {
      // Approvals badge just stays at its last known value too - same as the bell above.
    }
  }, []);

  // N1: refresh the bell badge whenever this screen regains focus, and whenever the app comes back
  // to the foreground while already on it - no polling interval (coordinator's explicit ask).
  useFocusEffect(
    useCallback(() => {
      void loadUnreadCount();
      void loadApprovalsCount();
    }, [loadUnreadCount, loadApprovalsCount])
  );

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextAppState) => {
      if (nextAppState === "active") {
        void loadUnreadCount();
        void loadApprovalsCount();
      }
    });
    return () => subscription.remove();
  }, [loadUnreadCount, loadApprovalsCount]);

  /** Every section loads independently (spec §4): one failing request never hides the others. */
  const loadAll = useCallback(async () => {
    const today = toLocalDateOnly(new Date());
    const lookaheadEnd = toLocalDateOnly(daysFromToday(LOOKAHEAD_DAYS));
    const [balancesResult, inboxResult, leaveResult, birthdaysResult, eventsResult, feedResult] =
      await Promise.allSettled([
        leaveService.getMyBalances(),
        approvalsService.getInbox(),
        homeFeedService.getLeaveCalendar(today, today),
        homeFeedService.getUpcomingBirthdays(LOOKAHEAD_DAYS),
        homeFeedService.getCompanyCalendar(today, lookaheadEnd),
        homeFeedService.getAnnouncementFeed(),
      ]);

    if (balancesResult.status === "fulfilled") {
      setBalances(sortBalancesAnnualFirst(balancesResult.value.balances));
      setBalanceError(false);
    } else {
      setBalanceError(true);
    }
    // Approvals badge: a failed inbox read keeps the last known count (spec §4: "hata sessiz").
    if (inboxResult.status === "fulfilled") {
      setApprovalsCount(inboxResult.value.length);
    }
    setSections({
      peopleOff: sectionFrom(leaveResult, (items) => selectPeopleOffToday(items, selfEmployeeId)),
      birthdays: sectionFrom(birthdaysResult, (items) => selectUpcomingBirthdays(items, today)),
      events: sectionFrom(eventsResult, selectUpcomingEvents),
      announcements: sectionFrom(feedResult, selectAnnouncements),
    });
  }, [selfEmployeeId]);

  // Re-fetched on a language switch too: leave type names and the people-off labels are localized
  // at load time (server-side via Accept-Language, client-side in `selectPeopleOffToday`).
  useEffect(() => {
    void loadAll();
  }, [loadAll, i18n.language]);

  async function onRefresh() {
    setRefreshing(true);
    await Promise.all([loadAll(), loadUnreadCount()]);
    setRefreshing(false);
  }

  function onMenuSelect(action: HomeMenuAction) {
    switch (action) {
      case "profile":
        navigation.navigate("Profile");
        break;
      case "notificationPreferences":
        navigation.navigate("NotificationPreferences");
        break;
      case "support":
        navigation.navigate("Support");
        break;
      case "logout":
        setLogoutConfirmVisible(true);
        break;
    }
  }

  const firstName = user?.firstName ?? "";
  const fullName = `${user?.firstName ?? ""} ${user?.lastName ?? ""}`.trim();
  const bellLabel =
    unreadCount > 0
      ? t("topBar.notificationsUnread", { count: unreadCount })
      : t("topBar.notifications");

  return (
    <Screen withTabBar refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <View style={styles.topBar}>
        <View style={styles.brand}>
          <BrandMark size={30} animated={false} />
          <Text style={styles.wordmark} accessibilityRole="header">
            Sense<Text style={styles.wordmarkAccent}>HR</Text>
          </Text>
        </View>
        <View style={styles.topActions}>
          <IconCircleButton
            icon="calendar-outline"
            onPress={() => navigation.navigate("Leave")}
            accessibilityLabel={t("topBar.myLeaves")}
          />
          <IconCircleButton
            icon="notifications-outline"
            onPress={() => navigation.navigate("Notifications")}
            accessibilityLabel={bellLabel}
            badge={unreadCount > 0 ? formatUnreadBadge(unreadCount) : undefined}
          />
          <IconCircleButton
            icon="ellipsis-vertical"
            onPress={() => setMenuVisible(true)}
            accessibilityLabel={t("topBar.moreOptions")}
          />
        </View>
      </View>

      <View style={styles.hello}>
        <TouchableOpacity
          onPress={() => navigation.navigate("Profile")}
          accessibilityRole="button"
          accessibilityLabel={t("hello.myProfile")}
        >
          <Avatar name={fullName} self size={60} radius={18} />
        </TouchableOpacity>
        <View style={styles.helloText}>
          <Text style={styles.greeting} numberOfLines={1}>
            {t("hello.greeting", { name: firstName })}
          </Text>
          <TouchableOpacity
            onPress={() => navigation.navigate("Profile")}
            accessibilityRole="link"
            accessibilityLabel={t("hello.viewProfile")}
            hitSlop={{ top: 12, bottom: 12, left: 4, right: 12 }}
          >
            <Text style={styles.profileLink}>{t("hello.viewProfile")}</Text>
          </TouchableOpacity>
        </View>
      </View>

      <SectionHeader title={t("sections.balances")} />
      {balanceError ? (
        <ErrorBanner message={t("balances.loadError")} onRetry={() => void onRefresh()} />
      ) : balances === null ? (
        <ActivityIndicator style={styles.balanceLoading} color={palette.white} />
      ) : balances.length === 0 ? (
        <Text style={styles.emptyBalances}>{t("balances.empty")}</Text>
      ) : (
        <BalanceCarousel items={balances} onPressItem={() => navigation.navigate("Leave")} />
      )}

      <SectionHeader title={t("sections.actions")} />
      <ActionGrid
        approvalsCount={approvalsCount}
        onNavigate={(route) => navigation.navigate(route)}
      />

      {sections.peopleOff ? (
        <>
          <SectionHeader title={t("sections.peopleOff")} />
          <PeopleOffCard people={sections.peopleOff} />
        </>
      ) : null}

      {sections.birthdays ? (
        <>
          <SectionHeader title={t("sections.birthdays")} />
          <BirthdayCard birthdays={sections.birthdays} />
        </>
      ) : null}

      {sections.events ? (
        <>
          <SectionHeader title={t("sections.events")} />
          {sections.events.map((event, index) => (
            <EventCard key={event.id ?? `${event.date}-${index}`} event={event} />
          ))}
        </>
      ) : null}

      {sections.announcements ? (
        <>
          <SectionHeader title={t("sections.announcements")} />
          {sections.announcements.map((announcement) => (
            <AnnouncementCard key={announcement.id} announcement={announcement} />
          ))}
        </>
      ) : null}

      <HomeMenu
        visible={menuVisible}
        onClose={() => setMenuVisible(false)}
        onSelect={onMenuSelect}
      />
      <LogoutConfirmSheet
        visible={logoutConfirmVisible}
        onClose={() => setLogoutConfirmVisible(false)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  topBar: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingHorizontal: 20,
    paddingTop: 8,
    paddingBottom: 4,
  },
  brand: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  wordmark: {
    fontSize: 23,
    letterSpacing: -1,
    color: palette.white,
    ...fontFor(800),
  },
  wordmarkAccent: {
    color: palette.brand,
  },
  topActions: {
    flexDirection: "row",
    gap: 4,
  },
  hello: {
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
    paddingHorizontal: 20,
    paddingTop: 14,
    paddingBottom: 6,
  },
  helloText: {
    flex: 1,
    minWidth: 0,
  },
  greeting: {
    ...text.title,
  },
  profileLink: {
    marginTop: 4,
    fontSize: 14,
    color: palette.brandText,
    ...fontFor(700),
  },
  balanceLoading: {
    paddingVertical: 24,
  },
  emptyBalances: {
    ...text.bodyMuted,
    paddingHorizontal: 20,
    paddingBottom: 8,
  },
});
