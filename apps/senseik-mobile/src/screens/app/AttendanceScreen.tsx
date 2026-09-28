import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { EmptyState, ErrorBanner, GlassCard, Screen, ScreenHeader } from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import { formatMonthYear } from "@/components/form/dateRange";
import type { AppStackParamList } from "@/types/navigation";
import {
  attendanceService,
  type AttendanceDay,
  type MyTimesheetSummary,
} from "@/services/AttendanceService";
import { formatDays, toLocalDateOnly } from "@/lib/formatters";
import { palette, text } from "@/theme/theme";
import { AttendanceDayCard } from "./attendance/AttendanceDayCard";
import { PeriodStepper } from "./attendance/PeriodStepper";
import { PillButton } from "./attendance/PillButton";
import {
  ATTENDANCE_FILTERS,
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

function formatWorkedMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remaining = minutes % 60;
  return `${hours} sa ${remaining} dk`;
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
  const label = diffMinutes > 0 ? "Fazla mesai" : "Eksik mesai";
  return `${label}: ${formatWorkedMinutes(Math.abs(diffMinutes))}`;
}

/**
 * Puantajım (spec §5): month stepper + client-side "Filtre" (Tümü / Eksik / Anomali), the month's
 * worked days/time, then one card per recorded day. Giriş / Çıkış and Vardiyam are shortcuts.
 */
export default function AttendanceScreen() {
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
    setError(null);
    try {
      const range = monthRange(monthDate);
      const [summaryResult, daysResult] = await Promise.all([
        attendanceService.getMyTimesheetSummary(range),
        attendanceService.getMyAttendanceDays(range, true),
      ]);
      setSummary(summaryResult);
      setDays([...daysResult].sort((a, b) => a.date.localeCompare(b.date)));
    } catch {
      setError("Puantaj bilgileri yüklenemedi. Lütfen tekrar deneyin.");
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

  const header = <ScreenHeader title="Puantajım" />;

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
  const filterLabel = ATTENDANCE_FILTERS.find((option) => option.value === filter)?.label ?? "";

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <View style={styles.shortcuts}>
        <PillButton
          icon="stopwatch-outline"
          label="Giriş / Çıkış"
          onPress={() => navigation.navigate("MainTabs", { screen: "CheckIn" })}
        />
        <PillButton
          icon="calendar-outline"
          label="Vardiyam"
          onPress={() => navigation.navigate("Shift")}
        />
      </View>

      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      <View style={styles.periodRow}>
        <PeriodStepper
          label={formatMonthYear(monthDate.getFullYear(), monthDate.getMonth())}
          onPrev={goToPreviousMonth}
          onNext={goToNextMonth}
          prevLabel="Önceki ay"
          nextLabel="Sonraki ay"
        />
        <PillButton
          icon="funnel-outline"
          label={filter === "all" ? "Filtre" : filterLabel}
          active={filter !== "all"}
          onPress={() => setFilterSheetVisible(true)}
        />
      </View>

      <GlassCard style={styles.summary}>
        <View style={styles.summaryItem}>
          <Text style={styles.summaryLabel}>Çalışılan gün</Text>
          <Text style={styles.summaryValue}>{formatDays(summary?.workedDays ?? 0)}</Text>
        </View>
        <View style={styles.summaryDivider} />
        <View style={styles.summaryItem}>
          <Text style={styles.summaryLabel}>Çalışılan süre</Text>
          <Text style={styles.summaryValue}>
            {formatWorkedMinutes(summary?.workedMinutes ?? 0)}
          </Text>
        </View>
      </GlassCard>

      {visibleDays.length === 0 ? (
        <EmptyState
          icon="time-outline"
          title={
            days.length === 0 ? "Bu ay için kayıtlı puantaj günü yok." : "Bu filtreye uyan gün yok."
          }
        />
      ) : (
        visibleDays.map((day) => (
          // `key`d by `date`, not `id`: leave-only rows all share the empty-guid `id`.
          <AttendanceDayCard key={day.date} day={day} overtimeText={formatOvertimeUndertime(day)} />
        ))
      )}

      <OptionSheet
        visible={filterSheetVisible}
        title="Filtre"
        options={ATTENDANCE_FILTERS}
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
