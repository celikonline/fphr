import * as React from "react";
import { useTranslation } from "react-i18next";
import { de, enUS, tr } from "react-day-picker/locale";
import { CalendarDays, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import { Calendar } from "./calendar";
import { Popover, PopoverContent, PopoverTrigger } from "./popover";

const LOCALES = { tr, en: enUS, de } as const;

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

/** Popover calendar with month/year dropdowns; drop-in replacement for `<Input type="date">`. */
export function DatePicker({ id, value, onChange, min, max, disabled, placeholder, clearable, className }: DatePickerProps) {
  const { t, i18n } = useTranslation(["common"]);
  const [open, setOpen] = React.useState(false);
  const language = i18n.language.split("-")[0] as keyof typeof LOCALES;
  const selected = value ? parseDateOnly(value) : undefined;
  const today = new Date();

  const disabledDays = [
    ...(min ? [{ before: parseDateOnly(min) }] : []),
    ...(max ? [{ after: parseDateOnly(max) }] : []),
  ];

  return (
    <div className={cn("relative", className)}>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            id={id}
            type="button"
            disabled={disabled}
            className={cn(
              "flex h-10 w-full items-center gap-2 rounded-md border border-input bg-background px-3 py-2 text-left text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
              clearable && value && "pr-9"
            )}
          >
            <CalendarDays className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className={cn("flex-1 truncate", !value && "text-muted-foreground")}>
              {value ? formatDateOnly(value, i18n.language) : (placeholder ?? t("common:pickDate"))}
            </span>
          </button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="start">
          <Calendar
            mode="single"
            required
            locale={LOCALES[language] ?? tr}
            captionLayout="dropdown"
            startMonth={new Date(1930, 0)}
            endMonth={new Date(today.getFullYear() + 10, 11)}
            defaultMonth={selected ?? (min ? parseDateOnly(min) : undefined)}
            selected={selected}
            disabled={disabledDays}
            onSelect={(date) => {
              onChange(toDateOnly(date));
              setOpen(false);
            }}
          />
        </PopoverContent>
      </Popover>
      {clearable && value && !disabled && (
        <button
          type="button"
          aria-label={t("common:clear")}
          className="absolute right-2 top-1/2 -translate-y-1/2 rounded-sm p-1 text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          onClick={() => onChange("")}
        >
          <X className="h-3.5 w-3.5" />
        </button>
      )}
    </div>
  );
}
