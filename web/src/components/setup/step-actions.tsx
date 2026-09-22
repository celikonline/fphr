import { useTranslation } from "react-i18next";
import { Button, Group, Text } from "@mantine/core";

/** Props every setup wizard step receives from the wizard page. */
export interface SetupStepProps {
  onContinue: () => void;
  onBack?: () => void;
}

interface StepActionsProps {
  onBack?: () => void;
  /** Present on optional steps: moves on without saving. */
  onSkip?: () => void;
  submitLabel?: string;
  isSubmitting?: boolean;
  error?: string | null;
}

/** Shared footer of the wizard steps: error line + Back / Skip / primary submit (the parent form's submit button). */
export function StepActions({
  onBack,
  onSkip,
  submitLabel,
  isSubmitting = false,
  error,
}: StepActionsProps) {
  const { t } = useTranslation("setup");

  return (
    <>
      {error && (
        <Text size="sm" c="red" role="alert">
          {error}
        </Text>
      )}
      <Group justify="space-between" mt="md">
        <div>
          {onBack && (
            <Button type="button" variant="default" onClick={onBack} disabled={isSubmitting}>
              {t("actions.back")}
            </Button>
          )}
        </div>
        <Group gap="sm">
          {onSkip && (
            <Button type="button" variant="subtle" onClick={onSkip} disabled={isSubmitting}>
              {t("actions.skip")}
            </Button>
          )}
          <Button type="submit" loading={isSubmitting}>
            {submitLabel ?? t("actions.next")}
          </Button>
        </Group>
      </Group>
    </>
  );
}
