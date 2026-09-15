import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Check, ChevronsUpDown, X } from "lucide-react";
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
import { listEmployees } from "@/services/employees.service";
import { cn } from "@/lib/utils";

export interface PickedEmployee {
  id: string;
  displayName: string;
}

interface EmployeePickerProps {
  id?: string;
  value: PickedEmployee | null;
  onChange: (value: PickedEmployee | null) => void;
  /** Employee that must not be offered (e.g. the card's own employee as its manager). */
  excludeId?: string;
  placeholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
}

/** Searchable single-employee combobox backed by `GET /v1/employees?q` (active employees). */
export default function EmployeePicker({ id, value, onChange, excludeId, placeholder, allowClear, disabled }: EmployeePickerProps) {
  const { t } = useTranslation(["employees"]);
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(search.trim()), 250);
    return () => clearTimeout(timer);
  }, [search]);

  const { data, isFetching } = useQuery({
    queryKey: ["employees", "picker", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 10, q: debounced || undefined, status: "Active", sort: "displayName" }),
    enabled: open,
  });

  const items = (data?.items ?? []).filter((e) => e.id !== excludeId);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className="flex gap-1">
        <PopoverTrigger asChild>
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className={cn("w-full justify-between font-normal", !value && "text-muted-foreground")}
          >
            <span className="truncate">{value?.displayName ?? placeholder ?? t("employees:picker.placeholder")}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        {allowClear && value && !disabled && (
          <Button type="button" variant="ghost" size="icon" title={t("employees:picker.clear")} onClick={() => onChange(null)}>
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
      <PopoverContent className="w-[var(--radix-popover-trigger-width)] min-w-72 p-0" align="start">
        <Command shouldFilter={false} loop>
          <CommandInput autoFocus placeholder={t("employees:picker.search")} value={search} onValueChange={setSearch} />
          <CommandList className="max-h-64">
            {isFetching && items.length === 0 ? (
              <CommandLoading>{t("employees:loading")}</CommandLoading>
            ) : (
              <>
                <CommandEmpty>{t("employees:picker.empty")}</CommandEmpty>
                {items.length > 0 && (
                  <CommandGroup>
                    {items.map((employee) => (
                      <CommandItem
                        key={employee.id}
                        value={employee.id}
                        onSelect={() => {
                          onChange({ id: employee.id, displayName: employee.displayName });
                          setOpen(false);
                        }}
                      >
                        <Check className={cn("h-4 w-4", value?.id === employee.id ? "opacity-100" : "opacity-0")} />
                        <span className="min-w-0">
                          <span className="block truncate">{employee.displayName}</span>
                          {employee.workEmail && (
                            <span className="block truncate text-xs text-muted-foreground">{employee.workEmail}</span>
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
