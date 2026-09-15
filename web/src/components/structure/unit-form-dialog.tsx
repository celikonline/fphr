import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import SearchPicker, { type PickerOption } from "@/components/structure/search-picker";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { getApiErrorStatus } from "@/lib/api-error";
import type { Location } from "@/services/locations.service";
import {
  COMPANY_TYPE_CODE,
  assignCompanyHolidayCalendar,
  createCompany,
  createUnit,
  searchEmployees,
  updateUnit,
  type Company,
  type HolidayCalendar,
  type OrgUnit,
  type OrgUnitType,
} from "@/services/structure.service";

const NONE = "__none__";

interface UnitFormDialogProps {
  level: OrgUnitType;
  /** Undefined = create. */
  unit?: OrgUnit;
  /** Company row joined to a Company-level unit (edit only). */
  company?: Company;
  activeTypes: OrgUnitType[];
  units: OrgUnit[];
  locations: Location[] | undefined;
  calendars: HolidayCalendar[] | undefined;
  defaultCalendarId: string | null;
  canPickManager: boolean;
  canAssignCalendar: boolean;
  initialManager: PickerOption | null;
  onClose: () => void;
  onSaved: () => void;
}

export default function UnitFormDialog({
  level,
  unit,
  company,
  activeTypes,
  units,
  locations,
  calendars,
  defaultCalendarId,
  canPickManager,
  canAssignCalendar,
  initialManager,
  onClose,
  onSaved,
}: UnitFormDialogProps) {
  const { t } = useTranslation(["structure", "common"]);
  const isCompanyLevel = level.code === COMPANY_TYPE_CODE;
  const isCreate = !unit;
  const isCompanyCreate = isCreate && isCompanyLevel;

  const [name, setName] = useState(unit?.name ?? "");
  const [parentId, setParentId] = useState<string>(unit?.parentId ?? NONE);
  const [manager, setManager] = useState<PickerOption | null>(initialManager);
  const [managerHidden, setManagerHidden] = useState(!canPickManager);
  const [locationId, setLocationId] = useState<string>(unit?.locationId ?? NONE);
  const initialCalendar = isCreate ? defaultCalendarId : (company?.holidayCalendarId ?? null);
  const [calendarId, setCalendarId] = useState<string>(initialCalendar ?? NONE);
  const [submitted, setSubmitted] = useState(false);
  const [calendarTouched, setCalendarTouched] = useState(false);

  // Calendars may load after the dialog opens; apply the default once unless the user picked one.
  useEffect(() => {
    if (isCompanyCreate && !calendarTouched && defaultCalendarId) setCalendarId(defaultCalendarId);
  }, [isCompanyCreate, calendarTouched, defaultCalendarId]);

  const onManagerSearchError = useCallback((error: unknown) => {
    if (getApiErrorStatus(error) === 403) setManagerHidden(true);
  }, []);

  const typeById = useMemo(() => new Map(activeTypes.map((x) => [x.id, x])), [activeTypes]);
  const parentOptions = useMemo(
    () =>
      units
        .filter((candidate) => {
          if (candidate.id === unit?.id) return false;
          const type = typeById.get(candidate.typeId);
          if (!type) return false;
          return type.sortOrder < level.sortOrder || (level.allowsNesting && type.id === level.id);
        })
        .sort((a, b) => {
          const sa = typeById.get(a.typeId)?.sortOrder ?? 0;
          const sb = typeById.get(b.typeId)?.sortOrder ?? 0;
          return sa - sb || a.name.localeCompare(b.name);
        }),
    [units, typeById, level, unit?.id],
  );

  // Keep an assigned-but-now-inactive location selectable so editing never silently clears it.
  const locationOptions = useMemo(() => {
    const list = locations ?? [];
    if (unit?.locationId && !list.some((l) => l.id === unit.locationId)) {
      return [...list, { id: unit.locationId, name: t("structure:units.unknownLocation") } as Location];
    }
    return list;
  }, [locations, unit?.locationId, t]);

  const mutation = useMutation({
    mutationFn: async () => {
      const toId = (value: string) => (value === NONE ? null : value);
      if (isCompanyCreate) {
        await createCompany({ name: name.trim(), holidayCalendarId: toId(calendarId) });
        return;
      }
      const managerEmployeeId = managerHidden ? (unit?.managerEmployeeId ?? null) : (manager?.id ?? null);
      // When locations can't be listed, keep whatever the unit already has.
      const nextLocationId = locations ? toId(locationId) : (unit?.locationId ?? null);
      if (unit) {
        await updateUnit(unit.id, {
          name: name.trim(),
          parentId: isCompanyLevel ? null : toId(parentId),
          managerEmployeeId,
          locationId: nextLocationId,
          sortOrder: unit.sortOrder,
        });
        if (company && canAssignCalendar && toId(calendarId) !== (company.holidayCalendarId ?? null)) {
          await assignCompanyHolidayCalendar(company.id, toId(calendarId));
        }
      } else {
        await createUnit({
          typeId: level.id,
          parentId: toId(parentId),
          name: name.trim(),
          managerEmployeeId,
          locationId: nextLocationId,
        });
      }
    },
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(isCreate ? "structure:units.createSuccess" : "structure:units.updateSuccess"),
      });
      onSaved();
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const nameMissing = name.trim().length === 0;

  return (
    <Dialog open onOpenChange={(open) => !open && !mutation.isPending && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {isCreate
              ? t("structure:units.createTitle", { level: level.name })
              : t("structure:units.editTitle", { level: level.name })}
          </DialogTitle>
          {isCompanyCreate && <DialogDescription>{t("structure:companies.createDescription")}</DialogDescription>}
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            setSubmitted(true);
            if (!nameMissing) mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="unit-name">{t("structure:units.name")}</Label>
            <Input id="unit-name" autoFocus maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
            {submitted && nameMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
          </div>

          {isCompanyLevel && (isCompanyCreate || company) && (
            <div>
              <Label htmlFor="unit-calendar">{t("structure:companies.holidayCalendar")}</Label>
              <Select
                value={calendarId}
                onValueChange={(value) => {
                  setCalendarTouched(true);
                  setCalendarId(value);
                }}
                disabled={!calendars || (!isCompanyCreate && !canAssignCalendar)}
              >
                <SelectTrigger id="unit-calendar">
                  <SelectValue placeholder={t("structure:companies.selectCalendar")} />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("structure:companies.noCalendar")}</SelectItem>
                  {(calendars ?? []).map((calendar) => (
                    <SelectItem key={calendar.id} value={calendar.id}>
                      {calendar.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="mt-1 text-xs text-muted-foreground">{t("structure:companies.holidayCalendarHint")}</p>
            </div>
          )}

          {!isCompanyLevel && (
            <div>
              <Label htmlFor="unit-parent">{t("structure:units.parent")}</Label>
              <Select value={parentId} onValueChange={setParentId}>
                <SelectTrigger id="unit-parent">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("structure:units.noParent")}</SelectItem>
                  {parentOptions.map((option) => (
                    <SelectItem key={option.id} value={option.id}>
                      {option.name} · {typeById.get(option.typeId)?.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="mt-1 text-xs text-muted-foreground">{t("structure:units.parentHint")}</p>
            </div>
          )}

          {!isCompanyCreate && !managerHidden && (
            <div>
              <Label htmlFor="unit-manager">{t("structure:units.manager")}</Label>
              <SearchPicker
                id="unit-manager"
                queryKey={["structure", "employee-search"]}
                search={async (q) =>
                  (await searchEmployees(q)).map((e) => ({ id: e.id, label: e.displayName, description: e.workEmail }))
                }
                value={manager}
                onChange={setManager}
                placeholder={t("structure:units.selectManager")}
                onError={onManagerSearchError}
              />
            </div>
          )}

          {!isCompanyCreate && locations && (
            <div>
              <Label htmlFor="unit-location">{t("structure:units.location")}</Label>
              <Select value={locationId} onValueChange={setLocationId}>
                <SelectTrigger id="unit-location">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("structure:units.noLocation")}</SelectItem>
                  {locationOptions.map((location) => (
                    <SelectItem key={location.id} value={location.id}>
                      {location.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="mt-1 text-xs text-muted-foreground">{t("structure:units.locationHint")}</p>
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
