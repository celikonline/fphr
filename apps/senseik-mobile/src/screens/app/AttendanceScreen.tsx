import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import type { AppStackParamList } from "@/types/navigation";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  attendanceService,
  parseAnomalyFlags,
  type AttendanceDay,
  type MyTimesheetSummary,
} from "@/services/AttendanceService";

/** First and last `DateOnly` ("yyyy-MM-dd") of the month containing `reference`. */
function monthRange(reference: Date): { from: string; to: string } {
  const year = reference.getFullYear();
  const month = reference.getMonth();
  const from = new Date(year, month, 1);
  const to = new Date(year, month + 1, 0);
  return { from: toDateOnly(from), to: toDateOnly(to) };
}

function toDateOnly(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function formatMonthLabel(reference: Date): string {
  const label = reference.toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function formatDayLabel(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  const label = date.toLocaleDateString("tr-TR", {
    weekday: "short",
    day: "2-digit",
    month: "short",
  });
  return label.charAt(0).toUpperCase() + label.slice(1);
}

/** `TimeOnly` ("HH:mm:ss") → "HH:mm" for display. */
function formatTime(value: string | null | undefined): string {
  if (!value) return "—";
  return value.slice(0, 5);
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

export default function AttendanceScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [monthDate, setMonthDate] = useState(() => new Date());
  const [summary, setSummary] = useState<MyTimesheetSummary | null>(null);
  const [days, setDays] = useState<AttendanceDay[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const range = monthRange(monthDate);
      const [summaryResult, daysResult] = await Promise.all([
        attendanceService.getMyTimesheetSummary(range),
        attendanceService.getMyAttendanceDays(range),
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

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Puantajım</Text>

      <TouchableOpacity style={styles.checkInButton} onPress={() => navigation.navigate("CheckIn")}>
        <Text style={styles.checkInButtonText}>QR ile giriş / çıkış yap</Text>
      </TouchableOpacity>

      <TouchableOpacity style={styles.shiftButton} onPress={() => navigation.navigate("Shift")}>
        <Text style={styles.shiftButtonText}>Vardiyalarımı görüntüle</Text>
      </TouchableOpacity>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <View style={styles.monthSelector}>
        <TouchableOpacity style={styles.monthButton} onPress={goToPreviousMonth}>
          <Text style={styles.monthButtonText}>‹</Text>
        </TouchableOpacity>
        <Text style={styles.monthLabel}>{formatMonthLabel(monthDate)}</Text>
        <TouchableOpacity style={styles.monthButton} onPress={goToNextMonth}>
          <Text style={styles.monthButtonText}>›</Text>
        </TouchableOpacity>
      </View>

      <View style={styles.summaryRow}>
        <View style={[styles.card, styles.summaryCard]}>
          <Text style={styles.cardLabel}>Çalışılan gün</Text>
          <Text style={styles.cardValue}>{summary?.workedDays ?? 0}</Text>
        </View>
        <View style={[styles.card, styles.summaryCard]}>
          <Text style={styles.cardLabel}>Çalışılan süre</Text>
          <Text style={styles.cardValue}>{formatWorkedMinutes(summary?.workedMinutes ?? 0)}</Text>
        </View>
      </View>

      <Text style={styles.sectionTitle}>Günler</Text>
      {days.length === 0 ? (
        <Text style={styles.emptyText}>Bu ay için kayıtlı puantaj günü yok.</Text>
      ) : (
        days.map((day) => {
          const anomalyFlags = parseAnomalyFlags(day.anomalies);
          const overtimeText = formatOvertimeUndertime(day);
          return (
            <View key={day.id} style={styles.card}>
              <View style={styles.dayHeader}>
                <Text style={styles.cardLabel}>{formatDayLabel(day.date)}</Text>
                {anomalyFlags.length > 0 && (
                  <View style={styles.anomalyBadge}>
                    <Text style={styles.anomalyBadgeText}>Uyuşmazlık</Text>
                  </View>
                )}
              </View>
              <Text style={styles.cardValue}>
                {formatTime(day.checkIn)} – {formatTime(day.checkOut)}
              </Text>
              <Text style={styles.cardSubtext}>
                {formatWorkedMinutes(day.workedMinutes)} çalışıldı
              </Text>
              {overtimeText && <Text style={styles.cardSubtext}>{overtimeText}</Text>}
            </View>
          );
        })
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  checkInButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    padding: spacing.md,
    alignItems: "center",
    marginBottom: spacing.lg,
  },
  checkInButtonText: {
    ...typography.body,
    color: colors.white,
    fontWeight: "600",
  },
  shiftButton: {
    backgroundColor: colors.gray100,
    borderRadius: 8,
    padding: spacing.md,
    alignItems: "center",
    marginBottom: spacing.lg,
  },
  shiftButtonText: {
    ...typography.body,
    color: colors.primary,
    fontWeight: "600",
  },
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  monthSelector: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: colors.gray50,
    borderRadius: 12,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    marginBottom: spacing.md,
  },
  monthButton: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
  },
  monthButtonText: {
    ...typography.h4,
    color: colors.primary,
  },
  monthLabel: {
    ...typography.body,
    color: colors.text,
    fontWeight: "600",
  },
  summaryRow: {
    flexDirection: "row",
    gap: spacing.sm,
  },
  summaryCard: {
    flex: 1,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  cardValue: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xs,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  errorBanner: {
    backgroundColor: "#fdecea",
    borderRadius: 8,
    padding: spacing.md,
    marginBottom: spacing.md,
  },
  errorText: {
    ...typography.bodySm,
    color: colors.error,
  },
  dayHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  anomalyBadge: {
    backgroundColor: colors.warning,
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  anomalyBadgeText: {
    ...typography.caption,
    color: colors.white,
  },
});
