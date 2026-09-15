import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Check, ChevronsUpDown, Loader2, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";

export interface PickerOption {
  id: string;
  label: string;
  description?: string | null;
}

interface SearchPickerProps {
  id?: string;
  /** react-query key prefix; the debounced search text is appended. */
  queryKey: readonly unknown[];
  search: (q: string) => Promise<PickerOption[]>;
  value: PickerOption | null;
  onChange: (option: PickerOption | null) => void;
  placeholder: string;
  allowClear?: boolean;
  disabled?: boolean;
  /** Called when the search request fails (e.g. 403) so callers can hide the field. */
  onError?: (error: unknown) => void;
}

/** Async search combobox (popover + text search) used for person pickers. */
export default function SearchPicker({
  id,
  queryKey,
  search,
  value,
  onChange,
  placeholder,
  allowClear = true,
  disabled,
  onError,
}: SearchPickerProps) {
  const { t } = useTranslation(["structure", "common"]);
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const [debounced, setDebounced] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(text.trim()), 300);
    return () => clearTimeout(timer);
  }, [text]);

  const { data, isFetching, error } = useQuery({
    queryKey: [...queryKey, debounced],
    queryFn: () => search(debounced),
    enabled: open,
    retry: false,
  });

  useEffect(() => {
    if (error && onError) onError(error);
  }, [error, onError]);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className="flex items-center gap-1">
        <PopoverTrigger asChild>
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className="w-full justify-between font-normal"
          >
            <span className={cn("truncate", !value && "text-muted-foreground")}>{value?.label ?? placeholder}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        {allowClear && value && !disabled && (
          <Button type="button" variant="ghost" size="icon" title={t("structure:clearSelection")} onClick={() => onChange(null)}>
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
      <PopoverContent className="w-[var(--radix-popover-trigger-width)] min-w-72 p-2" align="start">
        <div className="relative mb-2">
          <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            autoFocus
            className="pl-8"
            placeholder={t("structure:searchPerson")}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
        </div>
        <div className="max-h-60 overflow-y-auto">
          {isFetching && !data ? (
            <div className="flex justify-center p-3">
              <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            </div>
          ) : error ? (
            <p className="p-3 text-center text-sm text-muted-foreground">{t("common:errorGeneric")}</p>
          ) : !data || data.length === 0 ? (
            <p className="p-3 text-center text-sm text-muted-foreground">{t("common:noResults")}</p>
          ) : (
            data.map((option) => (
              <button
                key={option.id}
                type="button"
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                onClick={() => {
                  onChange(option);
                  setOpen(false);
                }}
              >
                <Check className={cn("h-4 w-4 shrink-0", value?.id === option.id ? "opacity-100" : "opacity-0")} />
                <span className="min-w-0">
                  <span className="block truncate">{option.label}</span>
                  {option.description && (
                    <span className="block truncate text-xs text-muted-foreground">{option.description}</span>
                  )}
                </span>
              </button>
            ))
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
