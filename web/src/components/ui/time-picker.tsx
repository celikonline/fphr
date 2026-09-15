import * as React from "react";
import { useTranslation } from "react-i18next";
import { Clock } from "lucide-react";
import { cn } from "@/lib/utils";
import { Popover, PopoverContent, PopoverTrigger } from "./popover";

const pad = (n: number) => String(n).padStart(2, "0");
const HOURS = Array.from({ length: 24 }, (_, i) => pad(i));

export interface TimePickerProps {
  id?: string;
  name?: string;
  /** "HH:mm" (24h), or "" when empty — same contract as `<Input type="time">`. */
  value: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
  disabled?: boolean;
  /** Minute granularity of the list (default 5); a stored off-step minute is still shown. */
  minuteStep?: number;
  placeholder?: string;
  className?: string;
}

/** Popover with hour and minute columns; drop-in replacement for `<Input type="time">`, usable with react-hook-form `Controller`. */
export const TimePicker = React.forwardRef<HTMLButtonElement, TimePickerProps>(function TimePicker(
  { id, name, value, onChange, onBlur, disabled, minuteStep = 5, placeholder, className },
  ref
) {
  const { t } = useTranslation(["common"]);
  const [open, setOpen] = React.useState(false);
  const [hour = "", minute = ""] = value ? value.split(":") : [];

  const minutes = React.useMemo(() => {
    const list = Array.from({ length: Math.ceil(60 / minuteStep) }, (_, i) => pad(i * minuteStep));
    return minute && !list.includes(minute) ? [...list, minute].sort() : list;
  }, [minuteStep, minute]);

  const scrollIntoView = (el: HTMLButtonElement | null) => el?.scrollIntoView({ block: "center" });

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) onBlur?.();
      }}
    >
      <PopoverTrigger asChild>
        <button
          ref={ref}
          id={id}
          name={name}
          type="button"
          disabled={disabled}
          className={cn(
            "flex h-10 w-full items-center gap-2 rounded-md border border-input bg-background px-3 py-2 text-left text-sm tabular-nums ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
            className
          )}
        >
          <Clock className="h-4 w-4 shrink-0 text-muted-foreground" />
          <span className={cn("flex-1 truncate", !value && "text-muted-foreground")}>
            {value || (placeholder ?? t("common:pickTime"))}
          </span>
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-1" align="start">
        <div className="flex gap-1">
          {[
            {
              label: t("common:hour"),
              items: HOURS,
              selected: hour,
              pick: (h: string) => onChange(`${h}:${minute || "00"}`),
            },
            {
              label: t("common:minute"),
              items: minutes,
              selected: minute,
              pick: (m: string) => {
                onChange(`${hour || "00"}:${m}`);
                setOpen(false);
                onBlur?.();
              },
            },
          ].map((column) => (
            <div key={column.label} role="listbox" aria-label={column.label} className="flex max-h-56 w-14 flex-col gap-0.5 overflow-y-auto">
              {column.items.map((item) => {
                const selected = item === column.selected;
                return (
                  <button
                    key={item}
                    ref={selected ? scrollIntoView : undefined}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    onClick={() => column.pick(item)}
                    className={cn(
                      "rounded-md px-2 py-1.5 text-center text-sm tabular-nums transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                      selected ? "bg-primary text-primary-foreground" : "hover:bg-accent hover:text-accent-foreground"
                    )}
                  >
                    {item}
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  );
});
