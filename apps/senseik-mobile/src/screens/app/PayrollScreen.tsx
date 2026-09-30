import React, { useCallback, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import * as Sharing from "expo-sharing";
import { useTranslation } from "react-i18next";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { EmptyState, ErrorBanner, Screen, ScreenHeader } from "@/components/ui";
import { canAcknowledge, payslipService, type MyPayslip } from "@/services/PayslipService";
import { formatRelativeTime } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import { monthName } from "@/components/form/dateRange";
import { palette, type Tone } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { PayslipHeroCard } from "./payroll/PayslipHeroCard";
import { PayslipRow } from "./payroll/PayslipRow";

export function payslipPeriodLabel(payslip: Pick<MyPayslip, "year" | "month">): string {
  return `${String(payslip.month).padStart(2, "0")}/${payslip.year}`;
}

/** Liste satırındaki onay durumu metni (list_shows_ack_status). */
export function payslipStatusLabel(payslip: MyPayslip): string {
  if (payslip.objectionStatus === "pending") return i18n.t("payroll:status.objectionPending");
  if (payslip.objectionStatus === "accepted") return i18n.t("payroll:status.objectionAccepted");
  if (payslip.objectionStatus === "rejected") return i18n.t("payroll:status.objectionRejected");
  if (payslip.acknowledgedAt) return i18n.t("payroll:status.acknowledged");
  return i18n.t("payroll:status.pending");
}

/** Status pill tone matching `payslipStatusLabel`. */
function payslipStatusTone(payslip: MyPayslip): Tone {
  if (payslip.objectionStatus === "pending") return "info";
  if (payslip.objectionStatus === "rejected") return "danger";
  if (payslip.objectionStatus === "accepted") return "neutral";
  return payslip.acknowledgedAt ? "success" : "warning";
}

/** Bordro para birimiyle (yoksa TRY) cihaz locale'ine göre biçimler. */
export function formatMoney(amount: number, currency?: string | null): string {
  const code = currency ?? "TRY";
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: code,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${code}`;
  }
}

/**
 * TK-030 riskli alan: always the same generic (localized) message, regardless of the underlying failure
 * (403 not-in-scope, 404 `payroll.payslip.not_found`, network error, ...) - the parameter exists only
 * to document at the type level that the message never reads or forwards anything from the actual
 * error (no `getApiErrorMessage`/server `detail` here, unlike most other screens in this app).
 */
export function payslipsLoadErrorMessage(_error: unknown): string {
  return i18n.t("payroll:errors.load");
}

/** Same guarantee as `payslipsLoadErrorMessage`, for the "open one payslip's PDF" action. */
export function payslipOpenErrorMessage(_error: unknown): string {
  return i18n.t("payroll:errors.open");
}

/** Same guarantee as `payslipsLoadErrorMessage`, for the "okudum, onaylıyorum" action (TK-030 kabul kriteri 2). */
export function payslipAcknowledgeErrorMessage(_error: unknown): string {
  return i18n.t("payroll:errors.acknowledge");
}

/** "Onaylandı · 2 gün önce" once a payslip has an `acknowledgedAt` - reuses the notifications list's relative-time formatter. */
export function formatAcknowledgedLabel(acknowledgedAt: string): string {
  return i18n.t("payroll:status.acknowledgedAt", { time: formatRelativeTime(acknowledgedAt) });
}

/** Status pill shown on every non-featured month row (TK-030). */
export function payslipAcknowledgementStatus(acknowledgedAt: string | null): {
  label: string;
  tone: Tone;
} {
  return acknowledgedAt
    ? { label: i18n.t("payroll:status.acknowledged"), tone: "success" }
    : { label: i18n.t("payroll:status.pending"), tone: "warning" };
}

/** Month name of a 1-based `month` in the active language; the number itself when out of range. */
function payslipMonthName(month: number): string {
  return monthName(month - 1) ?? String(month);
}

/** "Eylül 2026" / "September 2026" from a `MyPayslipDto`'s 1-based `month`/`year`. */
export function formatPayslipPeriod(year: number, month: number): string {
  return `${payslipMonthName(month)} ${year}`;
}

/** A stable, human-readable filename for the downloaded/shared PDF - never includes amounts. */
export function buildPayslipFileName(payslip: Pick<MyPayslip, "year" | "month">): string {
  return `bordro-${payslip.year}-${String(payslip.month).padStart(2, "0")}.pdf`;
}

/** Double-tap guard: while a payslip is already opening, every row's tap is ignored. */
export function shouldIgnorePayslipTap(openingPayslipId: string | null): boolean {
  return openingPayslipId !== null;
}

/**
 * Downloads one payslip's PDF and hands it to the share sheet; failures surface as a generic alert.
 * Shared by PayrollScreen (hero card) and PayslipDetailScreen.
 */
export async function sharePayslipDocument(payslip: MyPayslip): Promise<void> {
  let fileUri: string | null = null;
  try {
    fileUri = await payslipService.downloadDocument(
      payslip.payslipId,
      buildPayslipFileName(payslip)
    );
    if (!(await Sharing.isAvailableAsync())) {
      Alert.alert(i18n.t("common:alerts.error"), i18n.t("payroll:errors.shareUnavailable"));
      return;
    }
    await Sharing.shareAsync(fileUri, { mimeType: "application/pdf" });
  } catch (openError) {
    Alert.alert(i18n.t("common:alerts.error"), payslipOpenErrorMessage(openError));
  } finally {
    // Best-effort PII cleanup (TK-030): the share sheet's promise resolves once the chosen viewer
    // has been launched, not once the user is done looking at it, so this can't be a hard
    // guarantee - but leaving the PDF in cache indefinitely is worse for a payroll document.
    if (fileUri) {
      payslipService.deleteCachedDocument(fileUri);
    }
  }
}

export default function PayrollScreen() {
  // Re-renders on a language switch; the module-level helpers above read `i18n` at call time.
  const { t } = useTranslation("payroll");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [payslips, setPayslips] = useState<MyPayslip[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openingPayslipId, setOpeningPayslipId] = useState<string | null>(null);
  const [acknowledgingPayslipId, setAcknowledgingPayslipId] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      setPayslips(await payslipService.getMyPayslips());
    } catch (loadError) {
      // 403/404/network: never show stale or partial payslip data alongside the error (TK-030 riskli
      // alan) - a failed load always clears the list, even on a pull-to-refresh of a previously
      // successful screen.
      setPayslips([]);
      setError(payslipsLoadErrorMessage(loadError));
    }
  }, []);

  // Reload on focus so an acknowledgement/objection made on PayslipDetail shows up on return.
  useFocusEffect(
    useCallback(() => {
      setLoading(true);
      void loadAll().finally(() => setLoading(false));
    }, [loadAll])
  );

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  function openDetail(payslip: MyPayslip) {
    navigation.navigate("PayslipDetail", { payslip });
  }

  async function openPayslip(payslip: MyPayslip) {
    if (shouldIgnorePayslipTap(openingPayslipId)) {
      return;
    }
    setOpeningPayslipId(payslip.payslipId);
    try {
      await sharePayslipDocument(payslip);
    } finally {
      setOpeningPayslipId(null);
    }
  }

  async function acknowledgePayslip(payslipId: string) {
    setAcknowledgingPayslipId(payslipId);
    try {
      await payslipService.acknowledge(payslipId);
      // Re-fetch instead of patching local state in place - the server is the source of truth for
      // `acknowledgedAt` (and picks up any other change since the last load).
      await loadAll();
    } catch (acknowledgeError) {
      Alert.alert(t("common:alerts.error"), payslipAcknowledgeErrorMessage(acknowledgeError));
    } finally {
      setAcknowledgingPayslipId(null);
    }
  }

  // B6-style irreversible action: confirm before acknowledging instead of acting on the first tap.
  function confirmAcknowledge(payslipId: string) {
    Alert.alert(t("acknowledge"), t("confirm.message"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      { text: t("confirm.confirm"), onPress: () => void acknowledgePayslip(payslipId) },
    ]);
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

  const [latest, ...rest] = payslips;
  const anyActionInFlight = openingPayslipId !== null || acknowledgingPayslipId !== null;

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      {!error && payslips.length === 0 ? (
        <EmptyState icon="wallet-outline" title={t("empty")} />
      ) : null}

      {latest ? (
        <PayslipHeroCard
          period={formatPayslipPeriod(latest.year, latest.month)}
          netAmount={formatMoney(latest.netPay, latest.currency)}
          isAcknowledged={!canAcknowledge(latest)}
          isOpening={openingPayslipId === latest.payslipId}
          isAcknowledging={acknowledgingPayslipId === latest.payslipId}
          onOpen={() => void openPayslip(latest)}
          onAcknowledge={() => confirmAcknowledge(latest.payslipId)}
          onDetails={() => openDetail(latest)}
        />
      ) : null}

      {rest.map((payslip) => (
        <PayslipRow
          key={payslip.payslipId}
          monthLabel={payslipMonthName(payslip.month)}
          year={payslip.year}
          netAmount={formatMoney(payslip.netPay, payslip.currency)}
          // Inline "Onayla" only while the payslip can still be acknowledged (not during a pending objection).
          isAcknowledged={!canAcknowledge(payslip)}
          statusLabel={payslipStatusLabel(payslip)}
          statusTone={payslipStatusTone(payslip)}
          isOpening={false}
          isAcknowledging={acknowledgingPayslipId === payslip.payslipId}
          disabled={anyActionInFlight}
          onOpen={() => openDetail(payslip)}
          onAcknowledge={() => confirmAcknowledge(payslip.payslipId)}
        />
      ))}
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
});
