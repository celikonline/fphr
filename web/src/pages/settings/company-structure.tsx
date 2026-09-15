import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, Building2, CalendarDays, MapPin, MoreHorizontal, Pencil, Plus, Search, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import LevelsPanel, { UNIT_TYPES_QUERY_KEY } from "@/components/structure/levels-panel";
import UnitFormDialog from "@/components/structure/unit-form-dialog";
import UnitAuthorizationDialog from "@/components/structure/unit-authorization-dialog";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { listLocations } from "@/services/locations.service";
import {
  BRANCH_TYPE_CODE,
  COMPANY_TYPE_CODE,
  assignCompanyHolidayCalendar,
  deactivateUnit,
  getEmployeeOption,
  listCompanies,
  listHolidayCalendars,
  listUnitTypes,
  listUnits,
  pickDefaultHolidayCalendar,
  type OrgUnit,
} from "@/services/structure.service";

const UNITS_QUERY_KEY = ["organization", "units"] as const;
const COMPANIES_QUERY_KEY = ["organization", "companies"] as const;
const CALENDARS_QUERY_KEY = ["organization", "holiday-calendars"] as const;
const NONE = "__none__";

type FormState = { unit?: OrgUnit } | null;

export default function CompanyStructurePage() {
  const canRead = usePermission("organization.units.read");
  if (!canRead) return <NoAccess />;
  return <CompanyStructure />;
}

function CompanyStructure() {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();

  const canManageLevels = usePermission("organization.unit_types.manage");
  const canCreateUnit = usePermission("organization.units.create");
  const canUpdateUnit = usePermission("organization.units.update");
  const canDeleteUnit = usePermission("organization.units.delete");
  const canCreateCompany = usePermission("organization.companies.create");
  const canReadCalendars = usePermission("organization.holiday_calendars.read");
  const canAssignCalendar = usePermission("organization.holiday_calendars.update");
  const canReadLocations = usePermission("organization.locations.read");
  const canReadEmployees = usePermission("employee.employees.read");
  const canReadAuthorizations = usePermission("identity.unit_authorizations.read");
  const canManageAuthorizations = usePermission("identity.unit_authorizations.manage");

  const typesQuery = useQuery({ queryKey: UNIT_TYPES_QUERY_KEY, queryFn: () => listUnitTypes(false) });
  const unitsQuery = useQuery({ queryKey: UNITS_QUERY_KEY, queryFn: () => listUnits(true) });
  const companiesQuery = useQuery({ queryKey: COMPANIES_QUERY_KEY, queryFn: listCompanies, retry: false });
  const calendarsQuery = useQuery({
    queryKey: CALENDARS_QUERY_KEY,
    queryFn: listHolidayCalendars,
    enabled: canReadCalendars,
    retry: false,
  });
  const locationsQuery = useQuery({
    queryKey: ["locations", "all"],
    queryFn: () => listLocations({ page: 1, pageSize: 200, sort: "name" }),
    enabled: canReadLocations,
    retry: false,
  });

  const types = useMemo(() => typesQuery.data ?? [], [typesQuery.data]);
  const activeTypes = useMemo(
    () => types.filter((x) => x.isActive).sort((a, b) => a.sortOrder - b.sortOrder),
    [types],
  );
  const units = useMemo(() => unitsQuery.data ?? [], [unitsQuery.data]);
  const locations = locationsQuery.data?.items;
  const calendars = calendarsQuery.data;

  const [selectedTypeId, setSelectedTypeId] = useState<string | null>(null);
  const selectedType = activeTypes.find((x) => x.id === selectedTypeId) ?? activeTypes[0];
  const isCompanyLevel = selectedType?.code === COMPANY_TYPE_CODE;

  const [search, setSearch] = useState("");
  const [form, setForm] = useState<FormState>(null);
  const [authorizeTarget, setAuthorizeTarget] = useState<OrgUnit | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<OrgUnit | null>(null);

  const unitById = useMemo(() => new Map(units.map((u) => [u.id, u])), [units]);
  const typeById = useMemo(() => new Map(types.map((x) => [x.id, x])), [types]);
  const companyByUnitId = useMemo(
    () => new Map((companiesQuery.data ?? []).map((c) => [c.orgUnitId, c])),
    [companiesQuery.data],
  );
  const unitCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const unit of units) counts[unit.typeId] = (counts[unit.typeId] ?? 0) + 1;
    return counts;
  }, [units]);

  const levelUnits = useMemo(() => {
    const q = search.trim().toLocaleLowerCase();
    return units
      .filter((u) => u.typeId === selectedType?.id)
      .filter((u) => !q || u.name.toLocaleLowerCase().includes(q))
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [units, selectedType?.id, search]);

  const managerIds = useMemo(
    () => [...new Set(levelUnits.map((u) => u.managerEmployeeId).filter((id): id is string => !!id))],
    [levelUnits],
  );
  const managerQueries = useQueries({
    queries: managerIds.map((id) => ({
      queryKey: ["structure", "employee", id],
      queryFn: () => getEmployeeOption(id),
      enabled: canReadEmployees,
      retry: false,
      staleTime: 5 * 60_000,
    })),
  });
  const managerNames = new Map<string, string>();
  managerIds.forEach((id, index) => {
    const name = managerQueries[index]?.data?.displayName;
    if (name) managerNames.set(id, name);
  });

  const showLocationColumn =
    !isCompanyLevel && (selectedType?.code === BRANCH_TYPE_CODE || levelUnits.some((u) => u.locationId));
  const locationName = (id: string | null) =>
    id ? (locations?.find((l) => l.id === id)?.name ?? t("structure:units.unknownLocation")) : "—";

  const invalidateUnits = () => {
    queryClient.invalidateQueries({ queryKey: UNITS_QUERY_KEY });
    queryClient.invalidateQueries({ queryKey: COMPANIES_QUERY_KEY });
  };

  const deactivateMutation = useMutation({
    mutationFn: deactivateUnit,
    onSuccess: () => {
      invalidateUnits();
      toast({ variant: "success", title: t("common:success"), description: t("structure:units.deactivateSuccess") });
      setDeactivateTarget(null);
    },
    onError: (error) => {
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: structureErrorMessage(error, t, undefined, { 409: "structure:errors.unitHasChildren" }),
      });
      setDeactivateTarget(null);
    },
  });

  const calendarMutation = useMutation({
    mutationFn: ({ companyId, calendarId }: { companyId: string; calendarId: string | null }) =>
      assignCompanyHolidayCalendar(companyId, calendarId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COMPANIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("structure:companies.calendarSuccess") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const canAddInLevel = isCompanyLevel ? canCreateCompany : canCreateUnit;
  const canAuthorize = canReadAuthorizations || canManageAuthorizations;
  const hasRowActions = canUpdateUnit || canDeleteUnit || canAuthorize;

  const isLoading = typesQuery.isLoading || unitsQuery.isLoading;

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Building2 className="h-6 w-6" /> {t("structure:title")}
          </h1>
          <p className="text-muted-foreground">{t("structure:subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {canReadCalendars && (
            <Button asChild variant="outline" size="sm">
              <Link to="/app/settings/company/holiday-calendars">
                <CalendarDays className="mr-2 h-4 w-4" /> {t("structure:holidayCalendarsTitle")}
              </Link>
            </Button>
          )}
          {canReadLocations && (
            <Button asChild variant="outline" size="sm">
              <Link to="/app/settings/company/locations">
                <MapPin className="mr-2 h-4 w-4" /> {t("structure:locationsLink")}
              </Link>
            </Button>
          )}
        </div>
      </div>

      {isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton className="h-72 w-full" />
          <Skeleton className="h-72 w-full" />
        </div>
      ) : typesQuery.isError || unitsQuery.isError ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-destructive">{t("structure:errors.loadFailed")}</CardContent>
        </Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[18rem_1fr]">
          <LevelsPanel
            types={types}
            selectedTypeId={selectedType?.id ?? null}
            onSelect={(id) => {
              setSelectedTypeId(id);
              setSearch("");
            }}
            canManage={canManageLevels}
            unitCounts={unitCounts}
          />

          {selectedType && (
            <Card>
              <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3 space-y-0 pb-4">
                <div>
                  <CardTitle className="text-lg">{selectedType.name}</CardTitle>
                  <p className="text-xs text-muted-foreground">
                    {t("structure:units.count", { count: unitCounts[selectedType.id] ?? 0 })}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <div className="relative w-56">
                    <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      className="pl-9"
                      placeholder={t("structure:units.searchPlaceholder")}
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                  </div>
                  {canAddInLevel && (
                    <Button type="button" onClick={() => setForm({})}>
                      <Plus className="mr-2 h-4 w-4" /> {t("structure:units.addNew")}
                    </Button>
                  )}
                </div>
              </CardHeader>
              <CardContent className="p-0">
                {levelUnits.length === 0 ? (
                  <p className="p-8 text-center text-sm text-muted-foreground">
                    {search ? t("common:noResults") : t("structure:units.empty", { level: selectedType.name })}
                  </p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>{t("structure:units.name")}</TableHead>
                        {!isCompanyLevel && <TableHead>{t("structure:units.parent")}</TableHead>}
                        <TableHead>{t("structure:units.manager")}</TableHead>
                        {showLocationColumn && <TableHead>{t("structure:units.location")}</TableHead>}
                        {isCompanyLevel && <TableHead>{t("structure:companies.holidayCalendar")}</TableHead>}
                        {hasRowActions && <TableHead className="w-12" />}
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {levelUnits.map((unit) => {
                        const parent = unit.parentId ? unitById.get(unit.parentId) : undefined;
                        const company = companyByUnitId.get(unit.id);
                        return (
                          <TableRow key={unit.id}>
                            <TableCell className="font-medium">{unit.name}</TableCell>
                            {!isCompanyLevel && (
                              <TableCell>
                                {parent ? (
                                  <span>
                                    {parent.name}
                                    <span className="ml-1 text-xs text-muted-foreground">
                                      · {typeById.get(parent.typeId)?.name}
                                    </span>
                                  </span>
                                ) : (
                                  "—"
                                )}
                              </TableCell>
                            )}
                            <TableCell>
                              {unit.managerEmployeeId
                                ? (managerNames.get(unit.managerEmployeeId) ?? t("structure:units.managerAssigned"))
                                : "—"}
                            </TableCell>
                            {showLocationColumn && <TableCell>{locationName(unit.locationId)}</TableCell>}
                            {isCompanyLevel && (
                              <TableCell>
                                {!company ? (
                                  "—"
                                ) : canAssignCalendar && calendars ? (
                                  <Select
                                    value={company.holidayCalendarId ?? NONE}
                                    disabled={calendarMutation.isPending}
                                    onValueChange={(value) =>
                                      calendarMutation.mutate({
                                        companyId: company.id,
                                        calendarId: value === NONE ? null : value,
                                      })
                                    }
                                  >
                                    <SelectTrigger className="h-8 w-60">
                                      <SelectValue />
                                    </SelectTrigger>
                                    <SelectContent>
                                      <SelectItem value={NONE}>{t("structure:companies.noCalendar")}</SelectItem>
                                      {calendars.map((calendar) => (
                                        <SelectItem key={calendar.id} value={calendar.id}>
                                          {calendar.name}
                                        </SelectItem>
                                      ))}
                                    </SelectContent>
                                  </Select>
                                ) : (
                                  (company.holidayCalendarName ??
                                  calendars?.find((c) => c.id === company.holidayCalendarId)?.name ??
                                  (company.holidayCalendarId ? t("structure:companies.calendarAssigned") : "—"))
                                )}
                              </TableCell>
                            )}
                            {hasRowActions && (
                              <TableCell className="text-right">
                                <DropdownMenu>
                                  <DropdownMenuTrigger asChild>
                                    <Button type="button" variant="ghost" size="icon" title={t("common:actions")}>
                                      <MoreHorizontal className="h-4 w-4" />
                                    </Button>
                                  </DropdownMenuTrigger>
                                  <DropdownMenuContent align="end">
                                    {canUpdateUnit && (
                                      <DropdownMenuItem onSelect={() => setForm({ unit })}>
                                        <Pencil className="mr-2 h-4 w-4" /> {t("common:edit")}
                                      </DropdownMenuItem>
                                    )}
                                    {canAuthorize && (
                                      <DropdownMenuItem onSelect={() => setAuthorizeTarget(unit)}>
                                        <ShieldCheck className="mr-2 h-4 w-4" /> {t("structure:authorization.title")}
                                      </DropdownMenuItem>
                                    )}
                                    {canDeleteUnit && (
                                      <>
                                        <DropdownMenuSeparator />
                                        <DropdownMenuItem
                                          className="text-destructive focus:text-destructive"
                                          onSelect={() => setDeactivateTarget(unit)}
                                        >
                                          <Ban className="mr-2 h-4 w-4" /> {t("structure:units.deactivate")}
                                        </DropdownMenuItem>
                                      </>
                                    )}
                                  </DropdownMenuContent>
                                </DropdownMenu>
                              </TableCell>
                            )}
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                )}
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {form && selectedType && (
        <UnitFormDialog
          level={selectedType}
          unit={form.unit}
          company={form.unit ? companyByUnitId.get(form.unit.id) : undefined}
          activeTypes={activeTypes}
          units={units}
          locations={canReadLocations ? locations : undefined}
          calendars={calendars}
          defaultCalendarId={calendars ? (pickDefaultHolidayCalendar(calendars)?.id ?? null) : null}
          canPickManager={canReadEmployees}
          canAssignCalendar={canAssignCalendar}
          initialManager={
            form.unit?.managerEmployeeId
              ? {
                  id: form.unit.managerEmployeeId,
                  label: managerNames.get(form.unit.managerEmployeeId) ?? t("structure:units.managerAssigned"),
                }
              : null
          }
          onClose={() => setForm(null)}
          onSaved={invalidateUnits}
        />
      )}

      {authorizeTarget && <UnitAuthorizationDialog unit={authorizeTarget} onClose={() => setAuthorizeTarget(null)} />}

      <ConfirmDialog
        open={deactivateTarget !== null}
        onOpenChange={(open) => !open && setDeactivateTarget(null)}
        title={t("structure:units.deactivateTitle")}
        description={t("structure:units.deactivateConfirm", { name: deactivateTarget?.name ?? "" })}
        confirmLabel={t("structure:units.deactivate")}
        onConfirm={() => deactivateTarget && deactivateMutation.mutate(deactivateTarget.id)}
        isLoading={deactivateMutation.isPending}
      />
    </div>
  );
}
