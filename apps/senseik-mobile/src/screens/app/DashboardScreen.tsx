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
import type { BottomTabNavigationProp } from "@react-navigation/bottom-tabs";
import { useAuthStore } from "@/store/authStore";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  leaveService,
  type EmployeeLeaveBalances,
  type LeaveBalanceItem,
} from "@/services/LeaveService";
import { attendanceService, type MyTimesheetSummary } from "@/services/AttendanceService";
import { formatDays, toLocalDateOnly } from "@/lib/formatters";
import type { MainTabsParamList } from "@/types/navigation";

/** First and last `DateOnly` ("yyyy-MM-dd") of the month containing `reference` (Puantajım kalıbıyla aynı). */
function currentMonthRange(): { from: string; to: string } {
  const now = new Date();
  const from = new Date(now.getFullYear(), now.getMonth(), 1);
  const to = new Date(now.getFullYear(), now.getMonth() + 1, 0);
  return { from: toLocalDateOnly(from), to: toLocalDateOnly(to) };
}

function formatWorkedMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remaining = minutes % 60;
  return `${hours} sa ${remaining} dk`;
}

/**
 * G-10-07: yalnız yıllık izin bakiyesini göstermek istiyoruz - olay bazlı izinler (evlilik, doğum,
 * mazeret ...) toplama karışmaz. `leaveTypeCode` ile yıllık türü ayırt edebiliyorsak onu döneriz;
 * ayırt edilemiyorsa (tenant'ın yıllık izin türü hiç tanımlı değilse) ilk 3 türü ayrı satır olarak
 * kartta listeleriz - kartın kendisi tek bir toplam göstermez.
 */
function annualLeaveBalance(balances: EmployeeLeaveBalances | null): LeaveBalanceItem | null {
  if (!balances) return null;
  return balances.balances.find((item) => item.leaveTypeCode === "annual") ?? null;
}

function firstLeaveBalances(
  balances: EmployeeLeaveBalances | null,
  count: number
): LeaveBalanceItem[] {
  if (!balances) return [];
  return balances.balances.slice(0, count);
}

export default function DashboardScreen() {
  const { user, logout } = useAuthStore();
  const navigation = useNavigation<BottomTabNavigationProp<MainTabsParamList>>();

  const [balances, setBalances] = useState<EmployeeLeaveBalances | null>(null);
  const [summary, setSummary] = useState<MyTimesheetSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const [balancesResult, summaryResult] = await Promise.all([
        leaveService.getMyBalances(),
        attendanceService.getMyTimesheetSummary(currentMonthRange()),
      ]);
      setBalances(balancesResult);
      setSummary(summaryResult);
    } catch {
      setError("Özet bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  const annualBalance = annualLeaveBalance(balances);

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Ana Sayfa</Text>
      {user && (
        <Text style={styles.welcome}>
          Merhaba, {user.firstName} {user.lastName}
        </Text>
      )}

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {loading ? (
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={colors.primary} />
        </View>
      ) : (
        <View style={styles.cardRow}>
          <TouchableOpacity
            style={[styles.card, styles.summaryCard]}
            onPress={() => navigation.navigate("Leave")}
          >
            {annualBalance ? (
              <>
                <Text style={styles.cardLabel}>Yıllık izin bakiyem</Text>
                <Text style={styles.cardValue}>
                  {formatDays(annualBalance.balance.availableBalance)} gün
                </Text>
                <Text style={styles.cardSubtext}>kullanılabilir</Text>
              </>
            ) : (
              <>
                <Text style={styles.cardLabel}>İzin bakiyelerim</Text>
                {firstLeaveBalances(balances, 3).length === 0 ? (
                  <Text style={styles.cardSubtext}>Bakiye kaydı yok</Text>
                ) : (
                  firstLeaveBalances(balances, 3).map((item) => (
                    <Text key={item.leaveTypeCode + item.leaveTypeName} style={styles.cardSubtext}>
                      {item.leaveTypeName}: {formatDays(item.balance.availableBalance)} gün
                    </Text>
                  ))
                )}
              </>
            )}
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.card, styles.summaryCard]}
            onPress={() => navigation.navigate("Attendance")}
          >
            <Text style={styles.cardLabel}>Bu ay puantaj</Text>
            <Text style={styles.cardValue}>{formatDays(summary?.workedDays ?? 0)} gün</Text>
            <Text style={styles.cardSubtext}>
              {formatWorkedMinutes(summary?.workedMinutes ?? 0)} çalışıldı
            </Text>
          </TouchableOpacity>
        </View>
      )}

      <TouchableOpacity style={styles.logoutButton} onPress={() => logout()}>
        <Text style={styles.logoutButtonText}>Çıkış yap</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    paddingVertical: spacing.xxxl,
    alignItems: "center",
    justifyContent: "center",
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.sm,
  },
  welcome: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginBottom: spacing.lg,
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
  cardRow: {
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
  },
  summaryCard: {
    flex: 1,
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
  logoutButton: {
    backgroundColor: colors.error,
    padding: spacing.md,
    borderRadius: 8,
    alignItems: "center",
  },
  logoutButtonText: {
    ...typography.button,
    color: colors.white,
  },
});
