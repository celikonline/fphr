import React from "react";
import { StyleSheet, Text } from "react-native";
import { useTranslation } from "react-i18next";
import Animated, { FadeInDown } from "react-native-reanimated";
import { BrandMark } from "@/components/auth/BrandMark";
import { Wordmark } from "@/components/auth/Wordmark";
import { sectionTitleText } from "@/components/ui/SectionHeader";
import { palette, text } from "@/theme/theme";

/** Login screen's animated hero (brand mark, eyebrow, wordmark, subtitle) above the credentials panel. */
export function LoginHero() {
  const { t } = useTranslation("auth");
  // Upper-cased with the active locale's casing (`sectionTitleText`) - never `textTransform`, which
  // Android applies with the device locale.
  const eyebrow = sectionTitleText(t("hero.eyebrow"));
  return (
    <Animated.View entering={FadeInDown.delay(250).duration(500)} style={styles.hero}>
      <BrandMark size={56} />
      <Text style={styles.eyebrow}>{eyebrow}</Text>
      <Wordmark />
      <Text style={styles.heroSubtitle}>{t("hero.subtitle")}</Text>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  hero: { paddingHorizontal: 12 },
  eyebrow: { ...text.section, marginTop: 20, fontSize: 11, letterSpacing: 2.4, color: palette.ice },
  heroSubtitle: { ...text.body, marginTop: 10, color: palette.ice },
});
