import { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Group, Modal, Select, Stack, Text, TextInput } from "@mantine/core";
import SearchPicker, { type PickerOption } from "@/components/structure/search-picker";
import ApprovalPoliciesSection, { useUnitApprovalPolicies } from "@/components/structure/approval-policies-section";
import { usePermission } from "@/components/permission-guard";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { getApiErrorStatus } from "@/lib/api-error";
import type { Location } from "@/services/locations.service";
import { getCostCenters } from "@/services/organization.service";
import {
  COMPANY_TYPE_CODE,
  assignCompanyHolidayCalendar,
  createCompany,
  setUnitApprovalPolicies,
  createUnit,
  searchEmployees,
  updateUnit,
  setUnitCostCenter,
  type Company,
  type HolidayCalendar,
  type OrgUnit,
  type OrgUnitType,
} from "@/services/structure.service";

const NONE = "__none__";
/** Hints render under the field, as before the Mantine migration. */
const HINT_BELOW: ("label" | "input" | "description" | "error")[] = ["label", "input", "description", "error"];

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
  const queryClient = useQueryClient();
  const isCompanyLevel = level.code === COMPANY_TYPE_CODE;
  const isCreate = !unit;
  const isCompanyCreate = isCreate && isCompanyLevel;
  const canViewCostCenters = usePermission("organization.cost_centers.view");

  const [name, setName] = useState(unit?.name ?? "");
  const [parentId, setParentId] = useState<string>(unit?.parentId ?? NONE);
  const [manager, setManager] = useState<PickerOption | null>(initialManager);
  const [managerHidden, setManagerHidden] = useState(!canPickManager);
  const [locationId, setLocationId] = useState<string>(unit?.locationId ?? NONE);
  const [costCenterId, setCostCenterId] = useState<string>(unit?.costCenterId ?? NONE);
  const costCentersQuery = useQuery({ queryKey: ["organization", "cost-centers"], queryFn: () => getCostCenters(true), enabled: canViewCostCenters });
  const initialCalendar = isCreate ? defaultCalendarId : (company?.holidayCalendarId ?? null);
  const [calendarId, setCalendarId] = useState<string>(initialCalendar ?? NONE);
  const [submitted, setSubmitted] = useState(false);
  // "Opsiyonel alanlar → Onay süreçleri": edit only (an unassigned policy needs an existing unit id).
  const approvalPolicies = useUnitApprovalPolicies(unit?.id);
  const [calendarTouched, setCalendarTouched] = useState(false);

  // Calendars may load after the dialog opens; apply the default unless the user picked one.
  // Adjusted during render (guarded, so it settles in one extra pass) instead of in an effect.
  if (isCompanyCreate && !calendarTouched && defaultCalendarId && calendarId !== defaultCalendarId) {
    setCalendarId(defaultCalendarId);
  }

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
  const unitLocationId = unit?.locationId;
  const locationOptions = useMemo(() => {
    const list = locations ?? [];
    if (unitLocationId && !list.some((l) => l.id === unitLocationId)) {
      return [...list, { id: unitLocationId, name: t("structure:units.unknownLocation") } as Location];
    }
    return list;
  }, [locations, unitLocationId, t]);

  const mutation = useMutation({
    mutationFn: async () => {
      const toId = (value: string) => (value === NONE ? null : value);
      if (isCompanyCreate) {
        const created = await createCompany({ name: name.trim(), holidayCalendarId: toId(calendarId) });
        if (canViewCostCenters && costCenterId !== NONE) await setUnitCostCenter(created.orgUnitId, costCenterId);
        return;
      }
      const managerEmployeeId = managerHidden ? (unit?.managerEmployeeId ?? null) : (manager?.id ?? null);
      // When locations can't be listed, keep whatever the unit already has.
      const nextLocationId = locations ? toId(locationId) : (unit?.locationId ?? null);
      if (unit) {
        if (approvalPolicies.changes.length > 0) {
          await setUnitApprovalPolicies(unit.id, approvalPolicies.changes);
        }
        await updateUnit(unit.id, {
          name: name.trim(),
          parentId: isCompanyLevel ? null : toId(parentId),
          managerEmployeeId,
          locationId: nextLocationId,
          sortOrder: unit.sortOrder,
        });
        if (canViewCostCenters) await setUnitCostCenter(unit.id, costCenterId === NONE ? null : costCenterId);
        if (company && canAssignCalendar && toId(calendarId) !== (company.holidayCalendarId ?? null)) {
          await assignCompanyHolidayCalendar(company.id, toId(calendarId));
        }
      } else {
        const created = await createUnit({
          typeId: level.id,
          parentId: toId(parentId),
          name: name.trim(),
          managerEmployeeId,
          locationId: nextLocationId,
        });
        if (canViewCostCenters && costCenterId !== NONE) await setUnitCostCenter(created.id, costCenterId);
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["structure", "unit-approval-policies"] });
      queryClient.invalidateQueries({ queryKey: ["organization", "employee-cost-center"] });
      queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });
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
    <Modal
      opened
      size="lg"
      onClose={() => !mutation.isPending && onClose()}
      title={
        isCreate
          ? t("structure:units.createTitle", { level: level.name })
          : t("structure:units.editTitle", { level: level.name })
      }
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (!nameMissing) mutation.mutate();
        }}
      >
        <Stack gap="md">
          {isCompanyCreate && (
            <Text size="sm" c="dimmed">
              {t("structure:companies.createDescription")}
            </Text>
          )}

          <TextInput
            id="unit-name"
            data-autofocus
            maxLength={200}
            label={t("structure:units.name")}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />

          {isCompanyLevel && (isCompanyCreate || company) && (
            <Select
              id="unit-calendar"
              label={t("structure:companies.holidayCalendar")}
              description={t("structure:companies.holidayCalendarHint")}
              inputWrapperOrder={HINT_BELOW}
              placeholder={t("structure:companies.selectCalendar")}
              data={[
                { value: NONE, label: t("structure:companies.noCalendar") },
                ...(calendars ?? []).map((calendar) => ({ value: calendar.id, label: calendar.name })),
              ]}
              value={calendarId}
              allowDeselect={false}
              onChange={(value) => {
                if (value === null) return;
                setCalendarTouched(true);
                setCalendarId(value);
              }}
              disabled={!calendars || (!isCompanyCreate && !canAssignCalendar)}
            />
          )}

          {!isCompanyLevel && (
            <Select
              id="unit-parent"
              label={t("structure:units.parent")}
              description={t("structure:units.parentHint")}
              inputWrapperOrder={HINT_BELOW}
              searchable
              nothingFoundMessage={t("common:noResults")}
              data={[
                { value: NONE, label: t("structure:units.noParent") },
                ...parentOptions.map((option) => ({
                  value: option.id,
                  label: `${option.name} · ${typeById.get(option.typeId)?.name ?? ""}`,
                })),
              ]}
              value={parentId}
              allowDeselect={false}
              onChange={(value) => value !== null && setParentId(value)}
            />
          )}

          {!isCompanyCreate && !managerHidden && (
            <SearchPicker
              id="unit-manager"
              label={t("structure:units.manager")}
              queryKey={["structure", "employee-search"]}
              search={async (q) =>
                (await searchEmployees(q)).map((e) => ({ id: e.id, label: e.displayName, description: e.workEmail }))
              }
              value={manager}
              onChange={setManager}
              placeholder={t("structure:units.selectManager")}
              onError={onManagerSearchError}
            />
          )}

          {!isCompanyCreate && locations && (
            <Select
              id="unit-location"
              label={t("structure:units.location")}
              description={t("structure:units.locationHint")}
              inputWrapperOrder={HINT_BELOW}
              searchable
              nothingFoundMessage={t("common:noResults")}
              data={[
                { value: NONE, label: t("structure:units.noLocation") },
                ...locationOptions.map((location) => ({ value: location.id, label: location.name })),
              ]}
              value={locationId}
              allowDeselect={false}
              onChange={(value) => value !== null && setLocationId(value)}
            />
          )}

          {canViewCostCenters && <Select
              id="unit-cost-center"
              label={t("structure:units.costCenter")}
              description={t("structure:units.costCenterHint")}
              inputWrapperOrder={HINT_BELOW}
              searchable
              nothingFoundMessage={t("common:noResults")}
              data={[
                { value: NONE, label: t("structure:units.inheritCostCenter") },
                ...(costCentersQuery.data ?? []).map((center) => ({
                  value: center.id,
                  label: `${center.code} · ${center.name}${center.isActive ? "" : ` · ${t("structure:units.inactiveCostCenter")}`}`,
                  disabled: !center.isActive && center.id !== unit?.costCenterId,
                })),
              ]}
              value={costCenterId}
              allowDeselect={false}
              onChange={(value) => value !== null && setCostCenterId(value)}
              disabled={costCentersQuery.isLoading}
          />}

          {unit && (
            <ApprovalPoliciesSection state={approvalPolicies} units={units} disabled={mutation.isPending} />
          )}

          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
