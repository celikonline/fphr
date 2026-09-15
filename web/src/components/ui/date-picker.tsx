import { useTranslation } from "react-i18next";
import { DatePickerInput } from "@mantine/dates";
import { CalendarDays } from "lucide-react";

export interface DatePickerProps {
  id?: string;
  /** Backend `DateOnly` string ("yyyy-MM-dd"), or "" when empty — same contract as `<Input type="date">`. */
  value: string;
  onChange: (value: string) => void;
  /** Earliest / latest selectable day, as "yyyy-MM-dd". */
  min?: string;
  max?: string;
  disabled?: boolean;
  placeholder?: string;
  /** Shows a clear button while a value is set (optional fields and filters). */
  clearable?: boolean;
  className?: string;
}

/** Mantine date picker input (click the calendar header for month/year levels); drop-in replacement for `<Input type="date">`. */
export function DatePicker({ id, value, onChange, min, max, disabled, placeholder, clearable, className }: DatePickerProps) {
  const { t, i18n } = useTranslation(["common"]);
  const language = (i18n.resolvedLanguage ?? i18n.language ?? "tr").split("-")[0];

  return (
    <DatePickerInput
      id={id}
      className={className}
      value={value || null}
      onChange={(next) => onChange(next ?? "")}
      minDate={min || undefined}
      maxDate={max || undefined}
      defaultDate={value || min || undefined}
      disabled={disabled}
      placeholder={placeholder ?? t("common:pickDate")}
      clearable={clearable}
      clearButtonProps={{ "aria-label": t("common:clear") }}
      valueFormat={language === "en" ? "MMM D, YYYY" : "D MMM YYYY"}
      leftSection={<CalendarDays size={16} />}
      leftSectionPointerEvents="none"
      popoverProps={{ withinPortal: true }}
    />
  );
}
