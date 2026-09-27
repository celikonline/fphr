import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import * as Sharing from "expo-sharing";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { payrollService, type MyPayslip } from "@/services/PayrollService";
import { formatCurrencyTRY } from "@/lib/formatters";

/**
 * TK-030 riskli alan: always the same generic Turkish message, regardless of the underlying failure
 * (403 not-in-scope, 404 `payroll.payslip.not_found`, network error, ...) - the parameter exists only
 * to document at the type level that the message never reads or forwards anything from the actual
 * error (no `getApiErrorMessage`/server `detail` here, unlike most other screens in this app).
 */
export function payslipsLoadErrorMessage(_error: unknown): string {
  return "Bordro pusulaları yüklenemedi. Lütfen tekrar deneyin.";
}

/** Same guarantee as `payslipsLoadErrorMessage`, for the "open one payslip's PDF" action. */
export function payslipOpenErrorMessage(_error: unknown): string {
  return "Bordro pusulası açılamadı. Lütfen tekrar deneyin.";
}

const TURKISH_MONTHS = [
  "Ocak",
  "Şubat",
  "Mart",
  "Nisan",
  "Mayıs",
  "Haziran",
  "Temmuz",
  "Ağustos",
  "Eylül",
  "Ekim",
  "Kasım",
  "Aralık",
];

/** "Eylül 2026" from a `MyPayslipDto`'s 1-based `month`/`year`. */
export function formatPayslipPeriod(year: number, month: number): string {
  const name = TURKISH_MONTHS[month - 1] ?? String(month);
  return `${name} ${year}`;
}

/** A stable, human-readable filename for the downloaded/shared PDF - never includes amounts. */
export function buildPayslipFileName(payslip: Pick<MyPayslip, "year" | "month">): string {
  return `bordro-${payslip.year}-${String(payslip.month).padStart(2, "0")}.pdf`;
}

/** Double-tap guard: while a payslip is already opening, every row's tap is ignored. */
export function shouldIgnorePayslipTap(openingPayslipId: string | null): boolean {
  return openingPayslipId !== null;
}

export default function PayrollScreen() {
  const [payslips, setPayslips] = useState<MyPayslip[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openingPayslipId, setOpeningPayslipId] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      setPayslips(await payrollService.getMyPayslips());
    } catch (loadError) {
      // 403/404/network: never show stale or partial payslip data alongside the error (TK-030 riskli
      // alan) - a failed load always clears the list, even on a pull-to-refresh of a previously
      // successful screen.
      setPayslips([]);
      setError(payslipsLoadErrorMessage(loadError));
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

  async function openPayslip(payslip: MyPayslip) {
    if (shouldIgnorePayslipTap(openingPayslipId)) {
      return;
    }
    setOpeningPayslipId(payslip.payslipId);
    let fileUri: string | null = null;
    try {
      fileUri = await payrollService.downloadMyPayslipDocument(
        payslip.payslipId,
        buildPayslipFileName(payslip)
      );
      const canShare = await Sharing.isAvailableAsync();
      if (!canShare) {
        Alert.alert("Hata", "Bu cihazda dosya açma desteklenmiyor.");
        return;
      }
      await Sharing.shareAsync(fileUri, { mimeType: "application/pdf" });
    } catch (openError) {
      Alert.alert("Hata", payslipOpenErrorMessage(openError));
    } finally {
      // Best-effort PII cleanup (TK-030): the share sheet's promise resolves once the chosen viewer
      // has been launched, not once the user is done looking at it, so this can't be a hard
      // guarantee - but leaving the PDF in cache indefinitely is worse for a payroll document.
      if (fileUri) {
        payrollService.deleteCachedPayslipDocument(fileUri);
      }
      setOpeningPayslipId(null);
    }
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
      <Text style={styles.title}>Bordro</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {!error && payslips.length === 0 ? (
        <Text style={styles.emptyText}>Henüz görüntülenebilir bordro pusulanız yok.</Text>
      ) : (
        payslips.map((payslip) => {
          const isOpening = openingPayslipId === payslip.payslipId;
          return (
            <TouchableOpacity
              key={payslip.payslipId}
              style={styles.card}
              onPress={() => void openPayslip(payslip)}
              disabled={openingPayslipId !== null}
            >
              <Text style={styles.cardPeriod}>{formatPayslipPeriod(payslip.year, payslip.month)}</Text>
              <Text style={styles.cardNet}>{formatCurrencyTRY(payslip.netAmount)}</Text>
              <Text style={styles.cardGross}>Brüt {formatCurrencyTRY(payslip.grossAmount)}</Text>
              {isOpening && (
                <View style={styles.cardLoading}>
                  <ActivityIndicator size="small" color={colors.primary} />
                </View>
              )}
            </TouchableOpacity>
          );
        })
      )}
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
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardPeriod: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  cardNet: {
    ...typography.h3,
    color: colors.text,
    marginTop: spacing.xs,
  },
  cardGross: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  cardLoading: {
    marginTop: spacing.sm,
    alignItems: "flex-start",
  },
});
