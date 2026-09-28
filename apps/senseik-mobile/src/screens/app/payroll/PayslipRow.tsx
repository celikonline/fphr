import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { GlassCard, StatusPill } from "@/components/ui";
import { palette, text, type Tone } from "@/theme/theme";

type Props = {
  monthLabel: string;
  year: number;
  netAmount: string;
  isAcknowledged: boolean;
  statusLabel: string;
  statusTone: Tone;
  isOpening: boolean;
  isAcknowledging: boolean;
  disabled: boolean;
  onOpen: () => void;
  onAcknowledge: () => void;
};

/** TK-030: one non-featured month row (spec §5 - month/year left, net amount + status right). */
export function PayslipRow({
  monthLabel,
  year,
  netAmount,
  isAcknowledged,
  statusLabel,
  statusTone,
  isOpening,
  isAcknowledging,
  disabled,
  onOpen,
  onAcknowledge,
}: Props) {
  return (
    <GlassCard
      style={styles.card}
      onPress={onOpen}
      accessibilityLabel={`${monthLabel} ${year} bordrosu, belgeyi aç`}
    >
      <View style={styles.row}>
        <View>
          <Text style={styles.month}>{monthLabel}</Text>
          <Text style={styles.year}>{year}</Text>
        </View>
        <View style={styles.right}>
          <Text style={styles.amount}>{netAmount}</Text>
          {isAcknowledged ? (
            <StatusPill label={statusLabel} tone={statusTone} />
          ) : (
            <TouchableOpacity
              onPress={onAcknowledge}
              disabled={disabled}
              accessibilityRole="button"
              accessibilityLabel="Okudum, onaylıyorum"
              // `StatusPill`'in görsel yüksekliği ~24px (paddingVertical 4 + caption lineHeight 16) -
              // hitSlop dokunma alanını >=44'e çıkarır.
              hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
            >
              <StatusPill
                label={isAcknowledging ? "Kaydediliyor…" : statusLabel}
                tone={statusTone}
              />
            </TouchableOpacity>
          )}
        </View>
      </View>
      {isOpening ? (
        <ActivityIndicator style={styles.loading} size="small" color={palette.white} />
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  month: {
    ...text.heading,
  },
  year: {
    ...text.caption,
    marginTop: 2,
  },
  right: {
    alignItems: "flex-end",
    gap: 6,
  },
  amount: {
    ...text.heading,
    fontVariant: ["tabular-nums"],
  },
  loading: {
    marginTop: 10,
  },
});
