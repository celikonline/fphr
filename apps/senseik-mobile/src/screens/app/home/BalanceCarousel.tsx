import React from "react";
import { ScrollView, StyleSheet, Text, View, useWindowDimensions } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard } from "@/components/ui";
import { formatDays } from "@/lib/formatters";
import type { LeaveBalanceItem } from "@/services/LeaveService";
import { fontFor, palette, text } from "@/theme/theme";
import { balanceFraction } from "./homeSections";

type Props = {
  items: LeaveBalanceItem[];
  onPressItem: (item: LeaveBalanceItem) => void;
};

/** Mockup `.bal`: each card is 78% of the screen so the next one peeks in. */
const CARD_WIDTH_RATIO = 0.78;
const CARD_GAP = 12;
const SIDE_PADDING = 20;
const BAR_WIDTH = 170;

/** "Kalan bakiyem": one snapping glass card per leave type (also reused by İzinlerim). */
export function BalanceCarousel({ items, onPressItem }: Props) {
  const { t } = useTranslation("home");
  const { width } = useWindowDimensions();
  const cardWidth = Math.round(width * CARD_WIDTH_RATIO);

  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      snapToInterval={cardWidth + CARD_GAP}
      decelerationRate="fast"
      contentContainerStyle={styles.content}
      accessibilityLabel={t("balances.a11y")}
    >
      {items.map((item) => (
        <BalanceCard
          key={item.balance.leaveTypeId}
          item={item}
          width={cardWidth}
          onPress={() => onPressItem(item)}
        />
      ))}
    </ScrollView>
  );
}

function BalanceCard({
  item,
  width,
  onPress,
}: {
  item: LeaveBalanceItem;
  width: number;
  onPress: () => void;
}) {
  const { t } = useTranslation("home");
  const { available, entitlement, progress } = balanceFraction(item.balance);
  const summary =
    entitlement === null
      ? t("balances.available", { count: available, value: formatDays(available) })
      : t("balances.fraction", {
          count: entitlement,
          value: formatDays(available),
          total: formatDays(entitlement),
        });

  return (
    <GlassCard
      onPress={onPress}
      accessibilityLabel={`${item.leaveTypeName}: ${summary}`}
      style={[styles.card, { width }]}
      padding={0}
    >
      <View style={styles.body}>
        <Text style={styles.title} numberOfLines={1}>
          {item.leaveTypeName}
        </Text>
        <Text style={styles.value} numberOfLines={1}>
          <Text style={styles.valueStrong}>{formatDays(available)}</Text>
          {entitlement === null
            ? t("balances.availableSuffix", { count: available })
            : t("balances.fractionSuffix", { count: entitlement, total: formatDays(entitlement) })}
        </Text>
        {entitlement !== null ? (
          <View style={styles.track}>
            <View style={[styles.fill, { width: `${progress * 100}%` }]} />
          </View>
        ) : null}
      </View>
      <Ionicons name="chevron-forward" size={20} color={palette.textFaint} />
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  content: {
    gap: CARD_GAP,
    paddingHorizontal: SIDE_PADDING,
    paddingTop: 4,
    paddingBottom: 14,
  },
  card: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingVertical: 16,
    paddingHorizontal: 18,
  },
  body: {
    flex: 1,
    minWidth: 0,
  },
  title: {
    ...text.heading,
    letterSpacing: -0.2,
  },
  value: {
    marginTop: 6,
    fontSize: 15,
    color: palette.textMuted,
    fontVariant: ["tabular-nums"],
    ...fontFor(600),
  },
  valueStrong: {
    fontSize: 24,
    letterSpacing: -0.5,
    color: palette.white,
    ...fontFor(800),
  },
  track: {
    marginTop: 10,
    width: BAR_WIDTH,
    maxWidth: "100%",
    height: 5,
    borderRadius: 3,
    backgroundColor: palette.line,
    overflow: "hidden",
  },
  fill: {
    height: "100%",
    borderRadius: 3,
    backgroundColor: palette.brand,
  },
});
