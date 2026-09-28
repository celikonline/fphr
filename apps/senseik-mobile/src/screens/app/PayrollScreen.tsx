import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import * as Sharing from "expo-sharing";
import { useTranslation } from "react-i18next";
import { EmptyState, ErrorBanner, Screen, ScreenHeader } from "@/components/ui";
import { payrollService, type MyPayslip } from "@/services/PayrollService";
import { formatCurrencyTRY, formatRelativeTime } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import { monthName } from "@/components/form/dateRange";
import { palette, type Tone } from "@/theme/theme";
import { PayslipHeroCard } from "./payroll/PayslipHeroCard";
import { PayslipRow } from "./payroll/PayslipRow";

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

export default function PayrollScreen() {
  // Re-renders on a language switch; the module-level helpers above read `i18n` at call time.
  const { t } = useTranslation("payroll");
  const [payslips, setPayslips] = useState<MyPayslip[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openingPayslipId, setOpeningPayslipId] = useState<string | null>(null);
  const [acknowledgingPayslipId, setAcknowledgingPayslipId] = useState<string | null>(null);

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
        Alert.alert(t("common:alerts.error"), t("errors.shareUnavailable"));
        return;
      }
      await Sharing.shareAsync(fileUri, { mimeType: "application/pdf" });
    } catch (openError) {
      Alert.alert(t("common:alerts.error"), payslipOpenErrorMessage(openError));
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

  async function acknowledgePayslip(payslipId: string) {
    setAcknowledgingPayslipId(payslipId);
    try {
      await payrollService.acknowledgePayslip(payslipId);
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
          netAmount={formatCurrencyTRY(latest.netAmount)}
          isAcknowledged={Boolean(latest.acknowledgedAt)}
          isOpening={openingPayslipId === latest.payslipId}
          isAcknowledging={acknowledgingPayslipId === latest.payslipId}
          onOpen={() => void openPayslip(latest)}
          onAcknowledge={() => confirmAcknowledge(latest.payslipId)}
        />
      ) : null}

      {rest.map((payslip) => {
        const status = payslipAcknowledgementStatus(payslip.acknowledgedAt);
        return (
          <PayslipRow
            key={payslip.payslipId}
            monthLabel={payslipMonthName(payslip.month)}
            year={payslip.year}
            netAmount={formatCurrencyTRY(payslip.netAmount)}
            isAcknowledged={Boolean(payslip.acknowledgedAt)}
            statusLabel={status.label}
            statusTone={status.tone}
            isOpening={openingPayslipId === payslip.payslipId}
            isAcknowledging={acknowledgingPayslipId === payslip.payslipId}
            disabled={anyActionInFlight}
            onOpen={() => void openPayslip(payslip)}
            onAcknowledge={() => confirmAcknowledge(payslip.payslipId)}
          />
        );
      })}
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
