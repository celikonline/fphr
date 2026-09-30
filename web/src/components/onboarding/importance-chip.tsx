import { useTranslation } from "react-i18next";
import { Badge } from "@mantine/core";
import type { OnboardingImportance } from "@/services/onboarding.service";

const COLORS: Record<OnboardingImportance, string> = {
  normal: "gray",
  high: "orange",
  critical: "red",
};

/** Importance is always shown as text; the colour only reinforces it. */
export function ImportanceChip({ importance = "normal" }: { importance?: OnboardingImportance }) {
  const { t } = useTranslation(["onboarding"]);
  return (
    <Badge size="xs" variant="light" color={COLORS[importance]}>
      {t(`onboarding:importance.${importance}`)}
    </Badge>
  );
}
