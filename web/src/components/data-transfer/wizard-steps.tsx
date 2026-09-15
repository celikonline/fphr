import { useTranslation } from "react-i18next";
import { Stepper } from "@mantine/core";

export const WIZARD_STEPS = ["file", "mapping", "options", "run", "result"] as const;
export type WizardStep = (typeof WIZARD_STEPS)[number];

interface WizardStepsProps {
  /** Steps currently in focus (mapping and options share one screen). */
  active: WizardStep[];
}

export default function WizardSteps({ active }: WizardStepsProps) {
  const { t } = useTranslation(["dataTransfer"]);
  const activeIndexes = active.map((step) => WIZARD_STEPS.indexOf(step));
  // Stepper has a single active index: the last focused step is "in progress", earlier focused steps
  // keep their number (instead of the check mark) so a shared screen reads as one focus group.
  const lastActive = Math.max(...activeIndexes);

  return (
    <Stepper active={lastActive} size="sm" contentPadding={0}>
      {WIZARD_STEPS.map((step, index) => {
        const isActive = activeIndexes.includes(index);
        return (
          <Stepper.Step
            key={step}
            label={t(`dataTransfer:steps.${step}`)}
            completedIcon={isActive ? index + 1 : undefined}
            aria-current={isActive ? "step" : undefined}
          />
        );
      })}
    </Stepper>
  );
}
