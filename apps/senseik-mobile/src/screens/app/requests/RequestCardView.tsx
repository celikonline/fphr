import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard, StatusPill } from "@/components/ui";
import { REQUEST_OPTIONS } from "@/lib/requestOptions";
import { accents, fontFor, palette, text } from "@/theme/theme";
import type { RequestCard } from "./requestCards";

type Props = {
  card: RequestCard;
  isWithdrawing: boolean;
  onWithdraw: () => void;
};

/** One Taleplerim card: type icon, title + date, amount/duration, status pill, optional withdraw. */
export function RequestCardView({ card, isWithdrawing, onWithdraw }: Props) {
  const { t } = useTranslation("leave");
  const option = REQUEST_OPTIONS.find((item) => item.kind === card.kind);
  const accent = accents[option?.accent ?? "gray"];

  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <View style={[styles.icon, { backgroundColor: accent.bg }]}>
          <Ionicons name={option?.icon ?? "document-text-outline"} size={20} color={accent.fg} />
        </View>
        <View style={styles.texts}>
          <Text style={styles.title} numberOfLines={1}>
            {card.title}
          </Text>
          {card.dateLabel ? <Text style={styles.caption}>{card.dateLabel}</Text> : null}
        </View>
        <View style={styles.right}>
          <Text style={styles.detail}>{card.detail}</Text>
          <StatusPill label={card.statusLabel} tone={card.tone} />
        </View>
      </View>
      {card.description ? <Text style={styles.description}>{card.description}</Text> : null}
      {card.lines.map((line, index) => (
        <Text key={`${card.key}-line-${index}`} style={styles.caption}>
          {line}
        </Text>
      ))}
      {card.canWithdraw ? (
        <TouchableOpacity
          style={styles.withdraw}
          onPress={onWithdraw}
          disabled={isWithdrawing}
          accessibilityRole="button"
          accessibilityLabel={t("withdraw.action")}
          // Visual height ~16px; hitSlop brings the tappable area to >=44.
          hitSlop={{ top: 14, bottom: 14, left: 8, right: 8 }}
        >
          {isWithdrawing ? (
            <ActivityIndicator size="small" color={palette.coral} />
          ) : (
            <Text style={styles.withdrawText}>{t("withdraw.action")}</Text>
          )}
        </TouchableOpacity>
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
    gap: 8,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  icon: {
    width: 40,
    height: 40,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
  },
  texts: {
    flex: 1,
    minWidth: 0,
  },
  title: {
    ...text.heading,
  },
  caption: {
    ...text.caption,
    marginTop: 2,
  },
  right: {
    alignItems: "flex-end",
    gap: 6,
  },
  detail: {
    ...text.heading,
    fontVariant: ["tabular-nums"],
  },
  description: {
    ...text.bodyMuted,
  },
  withdraw: {
    alignSelf: "flex-start",
    paddingVertical: 2,
  },
  withdrawText: {
    ...text.caption,
    color: palette.coral,
    ...fontFor(700),
  },
});
