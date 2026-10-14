import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import { EmptyState, ErrorBanner, GlassCard, Screen, ScreenHeader } from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import { formatMonthYear } from "@/components/form/dateRange";
import type { AppStackParamList } from "@/types/navigation";
import {
  attendanceService,
  type AttendanceDay,
  type MyTimesheetSummary,
} from "@/services/AttendanceService";
import { formatDays, formatDurationMinutes, toLocalDateOnly } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import { palette, text } from "@/theme/theme";
import { AttendanceDayCard } from "./attendance/AttendanceDayCard";
import { PeriodStepper } from "./attendance/PeriodStepper";
import { PillButton } from "./attendance/PillButton";
import {
  attendanceFilters,
  attendanceDayStatus,
  matchesAttendanceFilter,
  type AttendanceFilter,
} from "./attendance/attendanceStatus";

/** First and last `DateOnly` ("yyyy-MM-dd") of the month containing `reference`. */
function monthRange(reference: Date): { from: string; to: string } {
  const year = reference.getFullYear();
  const month = reference.getMonth();
  const from = new Date(year, month, 1);
  const to = new Date(year, month + 1, 0);
  return { from: toLocalDateOnly(from), to: toLocalDateOnly(to) };
}

/**
 * Fazla/eksik mesai: backend `AttendanceDayDto`'nun zaten döndürdüğü `plannedMinutes`/`workedMinutes`
 * farkından türetilir (yeni bir backend/DTO alanı gerekmez). Planlı gün değilse (`plannedMinutes` 0,
 * ör. resmi tatil/hafta sonu) veya fark yoksa gösterilecek bir şey olmadığı için `null` döner.
 */
export function formatOvertimeUndertime(
  day: Pick<AttendanceDay, "workedMinutes" | "plannedMinutes">
): string | null {
  if (day.plannedMinutes <= 0) return null;
  const diffMinutes = day.workedMinutes - day.plannedMinutes;
  if (diffMinutes === 0) return null;
  return i18n.t(diffMinutes > 0 ? "attendance:overtime" : "attendance:undertime", {
    duration: formatDurationMinutes(Math.abs(diffMinutes)),
  });
}

/**
 * Puantajım (spec §5): month stepper + client-side "Filtre" (Tümü / Eksik / Anomali), the month's
 * worked days/time, then one card per recorded day. Giriş / Çıkış and Vardiyam are shortcuts.
 */
export default function AttendanceScreen() {
  const { t } = useTranslation("attendance");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [monthDate, setMonthDate] = useState(() => new Date());
  const [summary, setSummary] = useState<MyTimesheetSummary | null>(null);
  const [days, setDays] = useState<AttendanceDay[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<AttendanceFilter>("all");
  const [filterSheetVisible, setFilterSheetVisible] = useState(false);

  const loadAll = useCallback(async () => {
    try {
      const range = monthRange(monthDate);
      const [summaryResult, daysResult] = await Promise.all([
        attendanceService.getMyTimesheetSummary(range),
        attendanceService.getMyAttendanceDays(range, true),
      ]);
      setSummary(summaryResult);
      setDays([...daysResult].sort((a, b) => a.date.localeCompare(b.date)));
      setError(null);
    } catch {
      setSummary(null);
      setDays([]);
      setError(i18n.t("attendance:loadError"));
    }
  }, [monthDate]);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  function goToPreviousMonth() {
    setMonthDate((current) => new Date(current.getFullYear(), current.getMonth() - 1, 1));
  }

  function goToNextMonth() {
    setMonthDate((current) => new Date(current.getFullYear(), current.getMonth() + 1, 1));
  }

  const header = <ScreenHeader title={t("title")} />;

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  const visibleDays = days.filter((day) =>
    matchesAttendanceFilter(attendanceDayStatus(day), filter)
  );
  const filterOptions = attendanceFilters();
  const filterLabel = filterOptions.find((option) => option.value === filter)?.label ?? "";

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <View style={styles.shortcuts}>
        <PillButton
          icon="stopwatch-outline"
          label={t("common:tabs.checkIn")}
          onPress={() => navigation.navigate("MainTabs", { screen: "CheckIn" })}
        />
        <PillButton
          icon="calendar-outline"
          label={t("home:actions.shift")}
          onPress={() => navigation.navigate("Shift")}
        />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      <View style={styles.periodRow}>
        <PeriodStepper
          label={formatMonthYear(monthDate.getFullYear(), monthDate.getMonth())}
          onPrev={goToPreviousMonth}
          onNext={goToNextMonth}
          prevLabel={t("common:calendar.previousMonth")}
          nextLabel={t("common:calendar.nextMonth")}
        />
        <PillButton
          icon="funnel-outline"
          label={filter === "all" ? t("filterTitle") : filterLabel}
          active={filter !== "all"}
          onPress={() => setFilterSheetVisible(true)}
        />
      </View>

      {error ? null : (
        <GlassCard style={styles.summary}>
          <View style={styles.summaryItem}>
            <Text style={styles.summaryLabel}>{t("summary.workedDays")}</Text>
            <Text style={styles.summaryValue}>{formatDays(summary?.workedDays ?? 0)}</Text>
          </View>
          <View style={styles.summaryDivider} />
          <View style={styles.summaryItem}>
            <Text style={styles.summaryLabel}>{t("summary.workedTime")}</Text>
            <Text style={styles.summaryValue}>
              {formatDurationMinutes(summary?.workedMinutes ?? 0)}
            </Text>
          </View>
        </GlassCard>
      )}

      {!error && visibleDays.length === 0 ? (
        <EmptyState
          icon="time-outline"
          title={days.length === 0 ? t("empty.month") : t("empty.filter")}
          message={t(days.length === 0 ? "empty.monthGuide" : "empty.filterGuide")}
          actionLabel={t(days.length === 0 ? "empty.requestCorrection" : "empty.clearFilter")}
          onAction={() =>
            days.length === 0
              ? navigation.navigate("NewRequest", { kind: "punchCorrection" })
              : setFilter("all")
          }
        />
      ) : !error ? (
        visibleDays.map((day) => (
          // `key`d by `date`, not `id`: leave-only rows all share the empty-guid `id`.
          <AttendanceDayCard
            key={day.date}
            day={day}
            overtimeText={formatOvertimeUndertime(day)}
            onRequestCorrection={(date) =>
              navigation.navigate("NewRequest", { kind: "punchCorrection", date })
            }
          />
        ))
      ) : null}

      <OptionSheet
        visible={filterSheetVisible}
        title={t("filterTitle")}
        options={filterOptions}
        selected={filter}
        onSelect={setFilter}
        onClose={() => setFilterSheetVisible(false)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  shortcuts: {
    flexDirection: "row",
    gap: 10,
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  periodRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingHorizontal: 20,
    paddingVertical: 12,
  },
  summary: {
    flexDirection: "row",
    alignItems: "center",
    marginHorizontal: 20,
    marginBottom: 14,
  },
  summaryItem: {
    flex: 1,
    gap: 4,
  },
  summaryDivider: {
    width: 1,
    alignSelf: "stretch",
    marginHorizontal: 14,
    backgroundColor: palette.line,
  },
  summaryLabel: {
    ...text.caption,
  },
  summaryValue: {
    ...text.number,
  },
});
