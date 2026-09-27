import React from "react";
import { StyleSheet, Text, View } from "react-native";
import type { LeaveBalanceItem } from "@/services/LeaveService";
import { formatDays } from "@/lib/formatters";
import { palette, radius, text } from "@/theme/theme";

/**
 * B8 (2. bulgu): the first breakdown used `totalAccrued`/`totalUsed`, which are LIFETIME sums - they
 * don't add up to `availableBalance`, which is period-scoped. The fields that do sum to it
 * (`LeaveBalanceEngine.cs:239-256`):
 *   availableBalance = carriedOverIntoPeriod + lastPeriodAccrued + extraGrants + adjustments
 *                       - currentPeriodUsed - futureApprovedInPeriod
 * Zero-valued items are omitted; `adjustments` keeps its sign (a negative correction shows "-").
 * `pendingDays` is NOT part of that sum (the server doesn't subtract it from `availableBalance`,
 * only validates new requests against it) - shown separately in parentheses so it reads as
 * informational, not as a term of the equation above.
 */
export function balanceBreakdown(item: Pick<LeaveBalanceItem, "balance" | "pendingDays">): string {
  const {
    carriedOverIntoPeriod,
    lastPeriodAccrued,
    extraGrants,
    adjustments,
    currentPeriodUsed,
    futureApprovedInPeriod,
  } = item.balance;
  const parts: string[] = [];
  if (carriedOverIntoPeriod !== 0) parts.push(`${formatDays(carriedOverIntoPeriod)} devreden`);
  if (lastPeriodAccrued !== 0) parts.push(`${formatDays(lastPeriodAccrued)} hak ediş`);
  if (extraGrants !== 0) parts.push(`${formatDays(extraGrants)} ek hak`);
  if (adjustments !== 0) {
    const sign = adjustments > 0 ? "+" : "-";
    parts.push(`${sign}${formatDays(Math.abs(adjustments))} düzeltme`);
  }
  if (currentPeriodUsed !== 0) parts.push(`${formatDays(currentPeriodUsed)} kullanılan`);
  if (futureApprovedInPeriod !== 0) parts.push(`${formatDays(futureApprovedInPeriod)} planlanan`);

  const summary = parts.join(" · ");
  const pendingDays = item.pendingDays ?? 0;
  if (pendingDays <= 0) {
    return summary;
  }
  const pendingSuffix = `(${formatDays(pendingDays)} onay bekliyor)`;
  return summary.length > 0 ? `${summary} ${pendingSuffix}` : pendingSuffix;
}

type Props = {
  item: LeaveBalanceItem;
};

/** One balance's detail (İzinlerim's balance sheet): kullanılabilir gün + dönem bazlı döküm (B8). */
export function LeaveBalanceCard({ item }: Props) {
  const breakdown = balanceBreakdown(item);
  return (
    <View style={styles.card}>
      <Text style={styles.cardLabel}>{item.leaveTypeName}</Text>
      <Text style={styles.cardValue}>
        {formatDays(item.balance.availableBalance)} gün kullanılabilir
      </Text>
      {breakdown ? <Text style={styles.cardSubtext}>{breakdown}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: 6,
    padding: 16,
    borderRadius: radius.card,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  cardLabel: {
    ...text.bodyMuted,
  },
  cardValue: {
    ...text.number,
  },
  cardSubtext: {
    ...text.caption,
  },
});
