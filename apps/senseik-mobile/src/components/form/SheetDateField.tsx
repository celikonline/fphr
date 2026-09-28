import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { SelectField } from "@/components/ui";
import { formatDateWithWeekday } from "@/lib/formatters";
import { DateRangeSheet } from "./DateRangeSheet";

type Props = {
  label: string;
  /** `DateOnly` ("yyyy-MM-dd"). */
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  minDate?: string;
};

/** Glass select field that opens the JS `DateRangeSheet` in single-day mode (overtime/expense/advance). */
export function SheetDateField({ label, value, onChange, required, minDate }: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <>
      <SelectField
        label={label}
        required={required}
        value={formatDateWithWeekday(value)}
        placeholder={t("dateSheet.placeholder")}
        onPress={() => setOpen(true)}
      />
      <DateRangeSheet
        visible={open}
        mode="single"
        from={value}
        to={value}
        minDate={minDate}
        onApply={(from) => onChange(from)}
        onClose={() => setOpen(false)}
      />
    </>
  );
}
