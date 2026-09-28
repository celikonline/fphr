import React, { useCallback, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View
} from "react-native";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { payslipService, type MyPayslip } from "@/services/PayslipService";
import type { AppStackParamList } from "@/types/navigation";

export function payslipPeriodLabel(payslip: Pick<MyPayslip, "year" | "month">): string {
  return `${String(payslip.month).padStart(2, "0")}/${payslip.year}`;
}

/** Liste satırındaki onay durumu metni (list_shows_ack_status). */
export function payslipStatusLabel(payslip: MyPayslip): string {
  if (payslip.objectionStatus === "pending") return "İtiraz inceleniyor";
  if (payslip.objectionStatus === "accepted") return "İtiraz kabul edildi";
  if (payslip.objectionStatus === "rejected") return "İtiraz reddedildi";
  if (payslip.acknowledgedAt) return "Onaylandı";
  return "Onay bekliyor";
}

/** Bordro para birimiyle (yoksa TRY) cihaz locale'ine göre biçimler. */
export function formatMoney(amount: number, currency?: string | null): string {
  const code = currency ?? "TRY";
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: code,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${code}`;
  }
}

export default function PayrollScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [payslips, setPayslips] = useState<MyPayslip[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setPayslips(await payslipService.getMyPayslips());
    } catch {
      setError("Bordro bilgileri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      setLoading(true);
      void load().finally(() => setLoading(false));
    }, [load])
  );

  const onRefresh = async () => {
    setRefreshing(true);
    await load();
    setRefreshing(false);
  };

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.primary} />
      </View>
    );
  }

  return (
    <FlatList
      style={styles.container}
      contentContainerStyle={styles.content}
      data={payslips}
      keyExtractor={(item) => item.payslipId}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
      ListHeaderComponent={error ? <Text style={styles.error}>{error}</Text> : undefined}
      ListEmptyComponent={
        error ? undefined : <Text style={styles.empty}>Henüz yayınlanmış bir bordronuz yok.</Text>
      }
      renderItem={({ item }) => (
        <TouchableOpacity
          style={styles.card}
          onPress={() => navigation.navigate("PayslipDetail", { payslip: item })}
          accessibilityRole="button"
        >
          <View style={styles.row}>
            <Text style={styles.period}>{payslipPeriodLabel(item)}</Text>
            <Text style={styles.net}>{formatMoney(item.netPay, item.currency)}</Text>
          </View>
          <Text style={styles.status}>{payslipStatusLabel(item)}</Text>
        </TouchableOpacity>
      )}
    />
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg },
  center: { flex: 1, justifyContent: "center", alignItems: "center", backgroundColor: colors.background },
  card: {
    backgroundColor: colors.white,
    borderRadius: 8,
    padding: spacing.md,
    marginBottom: spacing.md,
    borderWidth: 1,
    borderColor: colors.gray200
  },
  row: { flexDirection: "row", justifyContent: "space-between" },
  period: { ...typography.h4, color: colors.text },
  net: { ...typography.body, color: colors.text },
  status: { ...typography.bodySm, color: colors.textSecondary, marginTop: spacing.xs },
  empty: { ...typography.bodySm, color: colors.textSecondary, textAlign: "center", marginTop: spacing.xl },
  error: { ...typography.bodySm, color: colors.error, marginBottom: spacing.md }
});
