import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Check, ChevronsUpDown, Loader2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandLoading,
} from "@/components/ui/command";
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

/** Async search combobox (popover + server-side search, keyboard navigable) used for person pickers. */
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

  const options = data ?? [];

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
      <PopoverContent className="w-[var(--radix-popover-trigger-width)] min-w-72 p-0" align="start">
        <Command shouldFilter={false} loop>
          <CommandInput autoFocus placeholder={t("structure:searchPerson")} value={text} onValueChange={setText} />
          <CommandList className="max-h-60">
            {isFetching && !data ? (
              <CommandLoading>
                <div className="flex justify-center">
                  <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
                </div>
              </CommandLoading>
            ) : error ? (
              <p className="py-6 text-center text-sm text-muted-foreground">{t("common:errorGeneric")}</p>
            ) : (
              <>
                <CommandEmpty>{t("common:noResults")}</CommandEmpty>
                {options.length > 0 && (
                  <CommandGroup>
                    {options.map((option) => (
                      <CommandItem
                        key={option.id}
                        value={option.id}
                        onSelect={() => {
                          onChange(option);
                          setOpen(false);
                        }}
                      >
                        <Check className={cn("h-4 w-4", value?.id === option.id ? "opacity-100" : "opacity-0")} />
                        <span className="min-w-0">
                          <span className="block truncate">{option.label}</span>
                          {option.description && (
                            <span className="block truncate text-xs text-muted-foreground">{option.description}</span>
                          )}
                        </span>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
