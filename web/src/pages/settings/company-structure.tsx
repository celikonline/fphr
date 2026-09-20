import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, Building2, CalendarDays, MapPin, MoreHorizontal, Pencil, Plus, Search, ShieldCheck } from "lucide-react";
import {
  ActionIcon,
  Button,
  Card,
  Group,
  Menu,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
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
type TFn = (key: string, opts?: Record<string, unknown>) => string;

/** Aksiyon menüsü — masaüstü tablo satırı ve mobil kart listesi arasında paylaşılır (aynı koşullar/aynı öğeler). */
function UnitActionsMenu({
  t,
  canUpdateUnit,
  canAuthorize,
  canDeleteUnit,
  onEdit,
  onAuthorize,
  onDeactivate,
}: {
  t: TFn;
  canUpdateUnit: boolean;
  canAuthorize: boolean;
  canDeleteUnit: boolean;
  onEdit: () => void;
  onAuthorize: () => void;
  onDeactivate: () => void;
}) {
  return (
    <Menu position="bottom-end">
      <Tooltip label={t("common:actions")}>
        <Menu.Target>
          <ActionIcon variant="subtle" color="gray" aria-label={t("common:actions")}>
            <MoreHorizontal size={16} />
          </ActionIcon>
        </Menu.Target>
      </Tooltip>
      <Menu.Dropdown>
        {canUpdateUnit && (
          <Menu.Item leftSection={<Pencil size={16} />} onClick={onEdit}>
            {t("common:edit")}
          </Menu.Item>
        )}
        {canAuthorize && (
          <Menu.Item leftSection={<ShieldCheck size={16} />} onClick={onAuthorize}>
            {t("structure:authorization.title")}
          </Menu.Item>
        )}
        {canDeleteUnit && (
          <>
            <Menu.Divider />
            <Menu.Item color="red" leftSection={<Ban size={16} />} onClick={onDeactivate}>
              {t("structure:units.deactivate")}
            </Menu.Item>
          </>
        )}
      </Menu.Dropdown>
    </Menu>
  );
}

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
        description: structureErrorMessage(error, t),
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
    <div className="max-w-7xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2">
            <Group gap="xs" component="span">
              <Building2 size={24} /> {t("structure:title")}
            </Group>
          </Title>
          <Text c="dimmed">{t("structure:subtitle")}</Text>
        </div>
        <Group gap="xs">
          {canReadCalendars && (
            <Button
              component={Link}
              to="/app/settings/company/holiday-calendars"
              variant="default"
              size="sm"
              leftSection={<CalendarDays size={16} />}
            >
              {t("structure:holidayCalendarsTitle")}
            </Button>
          )}
          {canReadLocations && (
            <Button
              component={Link}
              to="/app/settings/company/locations"
              variant="default"
              size="sm"
              leftSection={<MapPin size={16} />}
            >
              {t("structure:locationsLink")}
            </Button>
          )}
        </Group>
      </Group>

      {isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton h={288} />
          <Skeleton h={288} />
        </div>
      ) : typesQuery.isError || unitsQuery.isError ? (
        <Card withBorder p="lg">
          <Text size="sm" c="red" ta="center">
            {t("structure:errors.loadFailed")}
          </Text>
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
            <Card withBorder padding="lg">
              <Group justify="space-between" gap="sm" mb="md">
                <div>
                  <Text size="lg" fw={600}>
                    {selectedType.name}
                  </Text>
                  <Text size="xs" c="dimmed">
                    {t("structure:units.count", { count: unitCounts[selectedType.id] ?? 0 })}
                  </Text>
                </div>
                <Group gap="xs">
                  <TextInput
                    w={224}
                    leftSection={<Search size={16} />}
                    placeholder={t("structure:units.searchPlaceholder")}
                    aria-label={t("structure:units.searchPlaceholder")}
                    value={search}
                    onChange={(e) => setSearch(e.currentTarget.value)}
                  />
                  {canAddInLevel && (
                    <Button type="button" leftSection={<Plus size={16} />} onClick={() => setForm({})}>
                      {t("structure:units.addNew")}
                    </Button>
                  )}
                </Group>
              </Group>
              <Card.Section withBorder>
                {levelUnits.length === 0 ? (
                  <Text size="sm" c="dimmed" ta="center" p="xl">
                    {search ? t("common:noResults") : t("structure:units.empty", { level: selectedType.name })}
                  </Text>
                ) : (
                  <>
                    {/* Geniş ekran: tablo */}
                    <div className="hidden md:block">
                      <Table.ScrollContainer minWidth={640}>
                        <Table highlightOnHover horizontalSpacing="md">
                          <Table.Thead>
                            <Table.Tr>
                              <Table.Th>{t("structure:units.name")}</Table.Th>
                              {!isCompanyLevel && <Table.Th>{t("structure:units.parent")}</Table.Th>}
                              <Table.Th>{t("structure:units.manager")}</Table.Th>
                              {showLocationColumn && <Table.Th>{t("structure:units.location")}</Table.Th>}
                              {isCompanyLevel && <Table.Th>{t("structure:companies.holidayCalendar")}</Table.Th>}
                              {hasRowActions && <Table.Th w={48} />}
                            </Table.Tr>
                          </Table.Thead>
                          <Table.Tbody>
                            {levelUnits.map((unit) => {
                              const parent = unit.parentId ? unitById.get(unit.parentId) : undefined;
                              const company = companyByUnitId.get(unit.id);
                              return (
                                <Table.Tr key={unit.id}>
                                  <Table.Td fw={500}>{unit.name}</Table.Td>
                                  {!isCompanyLevel && (
                                    <Table.Td>
                                      {parent ? (
                                        <span>
                                          {parent.name}
                                          <Text span size="xs" c="dimmed" ml={4}>
                                            · {typeById.get(parent.typeId)?.name}
                                          </Text>
                                        </span>
                                      ) : (
                                        "—"
                                      )}
                                    </Table.Td>
                                  )}
                                  <Table.Td>
                                    {unit.managerEmployeeId
                                      ? (managerNames.get(unit.managerEmployeeId) ??
                                        t("structure:units.managerAssigned"))
                                      : "—"}
                                  </Table.Td>
                                  {showLocationColumn && <Table.Td>{locationName(unit.locationId)}</Table.Td>}
                                  {isCompanyLevel && (
                                    <Table.Td>
                                      {!company ? (
                                        "—"
                                      ) : canAssignCalendar && calendars ? (
                                        <Select
                                          size="xs"
                                          w={240}
                                          aria-label={t("structure:companies.holidayCalendar")}
                                          data={[
                                            { value: NONE, label: t("structure:companies.noCalendar") },
                                            ...calendars.map((calendar) => ({
                                              value: calendar.id,
                                              label: calendar.name,
                                            })),
                                          ]}
                                          value={company.holidayCalendarId ?? NONE}
                                          allowDeselect={false}
                                          disabled={calendarMutation.isPending}
                                          onChange={(value) =>
                                            value !== null &&
                                            calendarMutation.mutate({
                                              companyId: company.id,
                                              calendarId: value === NONE ? null : value,
                                            })
                                          }
                                        />
                                      ) : (
                                        (company.holidayCalendarName ??
                                        calendars?.find((c) => c.id === company.holidayCalendarId)?.name ??
                                        (company.holidayCalendarId ? t("structure:companies.calendarAssigned") : "—"))
                                      )}
                                    </Table.Td>
                                  )}
                                  {hasRowActions && (
                                    <Table.Td ta="right">
                                      <UnitActionsMenu
                                        t={t}
                                        canUpdateUnit={canUpdateUnit}
                                        canAuthorize={canAuthorize}
                                        canDeleteUnit={canDeleteUnit}
                                        onEdit={() => setForm({ unit })}
                                        onAuthorize={() => setAuthorizeTarget(unit)}
                                        onDeactivate={() => setDeactivateTarget(unit)}
                                      />
                                    </Table.Td>
                                  )}
                                </Table.Tr>
                              );
                            })}
                          </Table.Tbody>
                        </Table>
                      </Table.ScrollContainer>
                    </div>

                    {/* Dar ekran (≤390px dahil): kart listesi */}
                    <div className="md:hidden">
                      <Stack gap="sm" p="sm">
                        {levelUnits.map((unit) => {
                          const parent = unit.parentId ? unitById.get(unit.parentId) : undefined;
                          const company = companyByUnitId.get(unit.id);
                          return (
                            <Card key={unit.id} withBorder padding="sm" radius="md">
                              <Group justify="space-between" wrap="nowrap" align="flex-start">
                                <Stack gap={2} className="min-w-0">
                                  <Text fw={500} size="sm" truncate>
                                    {unit.name}
                                  </Text>
                                  {!isCompanyLevel && (
                                    <Text size="xs" c="dimmed" truncate>
                                      {parent
                                        ? `${parent.name} · ${typeById.get(parent.typeId)?.name ?? ""}`
                                        : "—"}
                                    </Text>
                                  )}
                                </Stack>
                                {hasRowActions && (
                                  <UnitActionsMenu
                                    t={t}
                                    canUpdateUnit={canUpdateUnit}
                                    canAuthorize={canAuthorize}
                                    canDeleteUnit={canDeleteUnit}
                                    onEdit={() => setForm({ unit })}
                                    onAuthorize={() => setAuthorizeTarget(unit)}
                                    onDeactivate={() => setDeactivateTarget(unit)}
                                  />
                                )}
                              </Group>
                              <Stack gap={4} mt="xs">
                                <Text size="xs" c="dimmed">
                                  {t("structure:units.manager")}:{" "}
                                  {unit.managerEmployeeId
                                    ? (managerNames.get(unit.managerEmployeeId) ??
                                      t("structure:units.managerAssigned"))
                                    : "—"}
                                </Text>
                                {showLocationColumn && (
                                  <Text size="xs" c="dimmed">
                                    {t("structure:units.location")}: {locationName(unit.locationId)}
                                  </Text>
                                )}
                                {isCompanyLevel && (
                                  <Group gap={4} align="center" wrap="wrap">
                                    <Text size="xs" c="dimmed">
                                      {t("structure:companies.holidayCalendar")}:
                                    </Text>
                                    {!company ? (
                                      <Text size="xs" c="dimmed">
                                        —
                                      </Text>
                                    ) : canAssignCalendar && calendars ? (
                                      <Select
                                        size="xs"
                                        w={200}
                                        aria-label={t("structure:companies.holidayCalendar")}
                                        data={[
                                          { value: NONE, label: t("structure:companies.noCalendar") },
                                          ...calendars.map((calendar) => ({
                                            value: calendar.id,
                                            label: calendar.name,
                                          })),
                                        ]}
                                        value={company.holidayCalendarId ?? NONE}
                                        allowDeselect={false}
                                        disabled={calendarMutation.isPending}
                                        onChange={(value) =>
                                          value !== null &&
                                          calendarMutation.mutate({
                                            companyId: company.id,
                                            calendarId: value === NONE ? null : value,
                                          })
                                        }
                                      />
                                    ) : (
                                      <Text size="xs">
                                        {company.holidayCalendarName ??
                                          calendars?.find((c) => c.id === company.holidayCalendarId)?.name ??
                                          (company.holidayCalendarId ? t("structure:companies.calendarAssigned") : "—")}
                                      </Text>
                                    )}
                                  </Group>
                                )}
                              </Stack>
                            </Card>
                          );
                        })}
                      </Stack>
                    </div>
                  </>
                )}
              </Card.Section>
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
