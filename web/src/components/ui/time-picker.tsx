import * as React from "react";
import { useTranslation } from "react-i18next";
import { TimePicker as MantineTimePicker } from "@mantine/dates";
import { Clock } from "lucide-react";

export interface TimePickerProps {
  id?: string;
  name?: string;
  /** "HH:mm" (24h), or "" when empty — same contract as `<Input type="time">`. */
  value: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
  disabled?: boolean;
  /** Minute granularity of the dropdown list and arrow keys (default 5); a stored off-step minute is still shown. */
  minuteStep?: number;
  /** Kept for API compatibility; Mantine's segmented hour/minute fields show "--" placeholders. */
  placeholder?: string;
  className?: string;
}

/**
 * Mantine 24h time input with an hour/minute dropdown; drop-in replacement for `<Input type="time">`, usable with
 * react-hook-form `Controller` (the forwarded ref points at the hours field, so `setFocus` works).
 */
export const TimePicker = React.forwardRef<HTMLInputElement, TimePickerProps>(function TimePicker(
  { id, name, value, onChange, onBlur, disabled, minuteStep = 5, className },
  ref
) {
  const { t } = useTranslation(["common"]);

  return (
    <MantineTimePicker
      // The id goes on the hours field so an external `<label htmlFor>` focuses the input.
      hoursInputProps={id ? { id } : undefined}
      name={name}
      className={className}
      value={value}
      onChange={onChange}
      onBlur={() => onBlur?.()}
      disabled={disabled}
      format="24h"
      minutesStep={minuteStep}
      withDropdown
      hoursRef={ref}
      hoursInputLabel={t("common:hour")}
      minutesInputLabel={t("common:minute")}
      leftSection={<Clock size={16} />}
      leftSectionPointerEvents="none"
      popoverProps={{ withinPortal: true }}
    />
  );
});
