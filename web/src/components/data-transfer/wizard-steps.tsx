import { Fragment } from "react";
import { useTranslation } from "react-i18next";
import { Check } from "lucide-react";
import { cn } from "@/lib/utils";

export const WIZARD_STEPS = ["file", "mapping", "options", "run", "result"] as const;
export type WizardStep = (typeof WIZARD_STEPS)[number];

interface WizardStepsProps {
  /** Steps currently in focus (mapping and options share one screen). */
  active: WizardStep[];
}

export default function WizardSteps({ active }: WizardStepsProps) {
  const { t } = useTranslation(["dataTransfer"]);
  const firstActive = Math.min(...active.map((step) => WIZARD_STEPS.indexOf(step)));

  return (
    <ol className="flex flex-wrap items-center gap-2 text-sm">
      {WIZARD_STEPS.map((step, index) => {
        const isActive = active.includes(step);
        const isDone = !isActive && index < firstActive;
        return (
          <Fragment key={step}>
            {index > 0 && <li aria-hidden className="h-px w-6 bg-border" />}
            <li
              className={cn(
                "flex items-center gap-2",
                isActive ? "font-semibold text-foreground" : isDone ? "text-foreground" : "text-muted-foreground"
              )}
              aria-current={isActive ? "step" : undefined}
            >
              <span
                className={cn(
                  "flex h-6 w-6 items-center justify-center rounded-full border text-xs",
                  isActive && "border-primary bg-primary text-primary-foreground",
                  isDone && "border-emerald-600 bg-emerald-600 text-white"
                )}
              >
                {isDone ? <Check className="h-3.5 w-3.5" /> : index + 1}
              </span>
              {t(`dataTransfer:steps.${step}`)}
            </li>
          </Fragment>
        );
      })}
    </ol>
  );
}
