import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { PrimaryButton } from "@/components/ui";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  period: string;
  netAmount: string;
  isAcknowledged: boolean;
  isOpening: boolean;
  isAcknowledging: boolean;
  onOpen: () => void;
  onAcknowledge: () => void;
};

/** TK-030: the newest payslip's highlighted card (spec §5 - brand-blue surface, white text). */
export function PayslipHeroCard({
  period,
  netAmount,
  isAcknowledged,
  isOpening,
  isAcknowledging,
  onOpen,
  onAcknowledge,
}: Props) {
  const { t } = useTranslation("payroll");
  return (
    <View style={styles.card}>
      <Text style={styles.caption}>{t("hero.caption")}</Text>
      <Text style={styles.period}>{period}</Text>
      <Text style={styles.amount}>{netAmount}</Text>
      <View style={styles.actions}>
        <PrimaryButton
          label={t("openDocument")}
          variant="secondary"
          loading={isOpening}
          onPress={onOpen}
        />
        {!isAcknowledged ? (
          <PrimaryButton
            label={t("acknowledge")}
            variant="secondary"
            loading={isAcknowledging}
            onPress={onAcknowledge}
          />
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
    padding: 20,
    borderRadius: radius.card,
    backgroundColor: palette.brand,
    gap: 4,
  },
  caption: {
    ...text.caption,
    color: palette.white,
  },
  period: {
    ...text.heading,
    color: palette.white,
  },
  amount: {
    ...text.number,
    fontSize: 30,
    lineHeight: 36,
    marginTop: 4,
    marginBottom: 12,
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
});
