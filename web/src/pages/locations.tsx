import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { MapPin, Pencil, Plus, QrCode, Search, Trash2 } from "lucide-react";
import { Button, Card, CopyButton, Group, Modal, NumberInput, Select, Stack, Text, TextInput, Title } from "@mantine/core";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { RowIconButton } from "@/components/ui/row-icon-button";
import ConfirmDialog from "@/components/confirm-dialog";
import LocationQrDialog from "@/components/locations/location-qr-dialog";
import PaginationFooter from "@/components/pagination-footer";
import { toast } from "@/hooks/use-toast";
import { useAnyPermission } from "@/components/permission-guard";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import {
  createLocation,
  deleteLocation,
  listLocations,
  regenerateLocationSelfCheckInCode,
  setLocationGeofence,
  updateLocation,
  LOCATION_TYPES,
  type Location,
  type LocationType,
} from "@/services/locations.service";

const MANAGE_PERMISSIONS = [
  "organization.locations.create",
  "organization.locations.update",
  "organization.locations.delete",
];

const DEFAULT_PAGE_SIZE = 10;
const COLUMN_VISIBILITY_KEY = "senseik.locations.grid.columns";

/** Fields the list endpoint can sort by (ListLocationsHandler.Sort). */
type LocationSortField = "name" | "type" | "countryCode";

/** Geofence alanları hep birlikte boş ya da hep birlikte dolu olmalı (backend Location.SetGeofence ile aynı kural); yarıçap merkez olmadan anlamsız. */
const schema = z
  .object({
    name: z.string().min(1),
    type: z.enum(LOCATION_TYPES as [LocationType, ...LocationType[]]),
    countryCode: z
      .string()
      .trim()
      .length(2)
      .transform((v) => v.toUpperCase()),
    address: z.string().optional(),
    sgkIsyeriSicilNo: z.string().optional(),
    latitude: z.number().min(-90).max(90).nullable(),
    longitude: z.number().min(-180).max(180).nullable(),
    radiusMeters: z.number().positive().nullable(),
  })
  .refine((v) => (v.latitude === null) === (v.longitude === null), { message: "geofence", path: ["longitude"] });

type FormValues = z.infer<typeof schema>;

interface DialogState {
  open: boolean;
  location?: Location;
}

export default function LocationsPage() {
  const { t } = useTranslation(["locations", "common"]);
  const queryClient = useQueryClient();
  const canManage = useAnyPermission(MANAGE_PERMISSIONS);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sort, setSort] = useState<{ field: LocationSortField; desc: boolean }>({ field: "name", desc: false });
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  // Back to the first page whenever the debounced search changes (adjusted during render, not in an effect).
  const [prevDebouncedSearch, setPrevDebouncedSearch] = useState(debouncedSearch);
  if (prevDebouncedSearch !== debouncedSearch) {
    setPrevDebouncedSearch(debouncedSearch);
    setPage(1);
  }

  const sortParam = `${sort.desc ? "-" : ""}${sort.field}`;

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["locations", page, pageSize, debouncedSearch, sortParam],
    queryFn: () => listLocations({ page, pageSize, q: debouncedSearch || undefined, sort: sortParam }),
    placeholderData: (previous) => previous,
  });

  const [dialog, setDialog] = useState<DialogState>({ open: false });
  const [deleteTarget, setDeleteTarget] = useState<Location | null>(null);
  const [regenerateTarget, setRegenerateTarget] = useState<Location | null>(null);
  const [qrTarget, setQrTarget] = useState<Location | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", type: "office", countryCode: "TR", address: "", sgkIsyeriSicilNo: "", latitude: null, longitude: null, radiusMeters: null },
  });

  function openCreate() {
    reset({ name: "", type: "office", countryCode: "TR", address: "", sgkIsyeriSicilNo: "", latitude: null, longitude: null, radiusMeters: null });
    setDialog({ open: true });
  }

  function openEdit(location: Location) {
    reset({
      name: location.name,
      type: location.type,
      countryCode: location.countryCode,
      address: location.address ?? "",
      sgkIsyeriSicilNo: location.sgkIsyeriSicilNo ?? "",
      latitude: location.latitude,
      longitude: location.longitude,
      radiusMeters: location.geofenceRadiusMeters,
    });
    setDialog({ open: true, location });
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["locations"] });

  /** Konum kaydından sonra geofence'i de senkronize eder (üçü boşsa temizler) - S-31 self-servis GPS check-in. */
  async function saveGeofence(location: Location, values: FormValues): Promise<Location> {
    return setLocationGeofence(location.id, { latitude: values.latitude, longitude: values.longitude, radiusMeters: values.radiusMeters });
  }

  const createMutation = useMutation({
    mutationFn: async (values: FormValues) => saveGeofence(await createLocation(values), values),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:createSuccess") });
      setDialog({ open: false });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("locations:genericError") }),
  });

  const updateMutation = useMutation({
    mutationFn: async ({ id, ...values }: { id: string } & FormValues) => saveGeofence(await updateLocation(id, values), values),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:updateSuccess") });
      setDialog({ open: false });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("locations:genericError") }),
  });

  const regenerateMutation = useMutation({
    mutationFn: regenerateLocationSelfCheckInCode,
    onSuccess: (updated) => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:regenerateSuccess") });
      setRegenerateTarget(null);
      setDialog((current) => (current.open && current.location?.id === updated.id ? { open: true, location: updated } : current));
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("locations:genericError") }),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteLocation,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:deleteSuccess") });
      setDeleteTarget(null);
    },
    onError: (error) => {
      toast({
        variant: "destructive",
        title: t("common:error"),
        description:
          getApiErrorStatus(error) === 409
            ? t("locations:inUseError")
            : getApiErrorMessage(error, t("locations:genericError")),
      });
      setDeleteTarget(null);
    },
  });

  function onSubmit(values: FormValues) {
    if (dialog.location) {
      updateMutation.mutate({ id: dialog.location.id, ...values });
    } else {
      createMutation.mutate(values);
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending;

  function changeSorting(next: SortingState) {
    const [first] = next;
    if (!first) return;
    setSort({ field: first.id as LocationSortField, desc: first.desc });
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const columns: ColumnDef<Location>[] = [
    {
      id: "name",
      accessorKey: "name",
      enableHiding: false,
      header: t("locations:name"),
      meta: { label: t("locations:name") },
      cell: ({ row }) => (
        <Text size="sm" fw={500}>
          {row.original.name}
        </Text>
      ),
    },
    {
      id: "type",
      accessorKey: "type",
      header: t("locations:type"),
      meta: { label: t("locations:type") },
      cell: ({ row }) => t(`locations:types.${row.original.type}`),
    },
    {
      id: "countryCode",
      accessorKey: "countryCode",
      header: t("locations:countryCode"),
      meta: { label: t("locations:countryCode") },
    },
    {
      id: "address",
      enableSorting: false,
      header: t("locations:address"),
      meta: { label: t("locations:address") },
      cell: ({ row }) => row.original.address ?? "—",
    },
    {
      id: "sgkIsyeriSicilNo",
      enableSorting: false,
      header: t("locations:sgkIsyeriSicilNo"),
      meta: { label: t("locations:sgkIsyeriSicilNo") },
      cell: ({ row }) => row.original.sgkIsyeriSicilNo ?? "—",
    },
    ...(canManage
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: t("common:actions"),
            meta: { headerClassName: "text-right", cellClassName: "text-right" },
            cell: ({ row }) => (
              <Group gap={4} justify="flex-end" wrap="nowrap" onClick={(e) => e.stopPropagation()}>
                <RowIconButton label={t("locations:editLocation")} icon={<Pencil size={16} />} onClick={() => openEdit(row.original)} />
                <RowIconButton
                  label={t("locations:deleteLocation")}
                  icon={<Trash2 size={16} />}
                  color="red"
                  onClick={() => setDeleteTarget(row.original)}
                />
              </Group>
            ),
          } satisfies ColumnDef<Location>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: data?.items ?? [],
    getRowId: (location) => location.id,
    sorting: [{ id: sort.field, desc: sort.desc }],
    onSortingChange: changeSorting,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
  });

  const typeOptions = LOCATION_TYPES.map((type) => ({ value: type, label: t(`locations:types.${type}`) }));
  const closeDialog = () => setDialog((prev) => ({ ...prev, open: false }));

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <MapPin size={24} /> {t("locations:title")}
          </Title>
          <Text c="dimmed">{t("locations:subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("locations:addLocation")}
          </Button>
        )}
      </Group>

      <div className="flex flex-wrap items-end gap-3">
        <TextInput
          className="w-full max-w-sm"
          leftSection={<Search size={16} />}
          placeholder={t("locations:searchPlaceholder")}
          aria-label={t("locations:searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        <DataTableViewOptions table={table} className="ml-auto" />
      </div>

      <Card withBorder padding={0}>
        {/* Geniş ekran: tablo */}
        <div className="hidden md:block">
          <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("locations:empty")} />
        </div>

        {/* Dar ekran (≤390px dahil): kart listesi */}
        <div className="p-3 md:hidden">
          {isLoading ? (
            <Text size="sm" c="dimmed">
              {t("common:loading")}
            </Text>
          ) : (data?.items.length ?? 0) === 0 ? (
            <Text size="sm" c="dimmed" ta="center" p="lg">
              {t("locations:empty")}
            </Text>
          ) : (
            <Stack gap="sm">
              {data?.items.map((location) => (
                <Card key={location.id} withBorder padding="sm" radius="md">
                  <Group justify="space-between" align="flex-start" wrap="nowrap">
                    <div>
                      <Text fw={500} size="sm">
                        {location.name}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t(`locations:types.${location.type}`)} · {location.countryCode}
                      </Text>
                      {location.address && (
                        <Text size="xs" c="dimmed">
                          {location.address}
                        </Text>
                      )}
                      {location.sgkIsyeriSicilNo && (
                        <Text size="xs" c="dimmed">
                          {t("locations:sgkIsyeriSicilNo")}: {location.sgkIsyeriSicilNo}
                        </Text>
                      )}
                    </div>
                    {canManage && (
                      <Group gap={4} wrap="nowrap">
                        <RowIconButton label={t("locations:editLocation")} icon={<Pencil size={16} />} onClick={() => openEdit(location)} />
                        <RowIconButton
                          label={t("locations:deleteLocation")}
                          icon={<Trash2 size={16} />}
                          color="red"
                          onClick={() => setDeleteTarget(location)}
                        />
                      </Group>
                    )}
                  </Group>
                </Card>
              ))}
            </Stack>
          )}
        </div>

        {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
      </Card>

      <Modal
        opened={dialog.open}
        onClose={closeDialog}
        title={dialog.location ? t("locations:dialogEditTitle") : t("locations:dialogCreateTitle")}
      >
        <form onSubmit={handleSubmit(onSubmit)}>
          <Stack gap="md">
            <TextInput
              id="loc-name"
              data-autofocus
              label={t("locations:name")}
              error={errors.name ? t("common:required") : undefined}
              {...register("name")}
            />
            <Controller
              control={control}
              name="type"
              render={({ field }) => (
                <Select
                  id="loc-type"
                  label={t("locations:type")}
                  data={typeOptions}
                  value={field.value}
                  onChange={(value) => value && field.onChange(value as LocationType)}
                  onBlur={field.onBlur}
                  allowDeselect={false}
                />
              )}
            />
            <TextInput
              id="loc-country"
              label={t("locations:countryCode")}
              maxLength={2}
              placeholder="TR"
              error={errors.countryCode ? t("locations:countryCodeInvalid") : undefined}
              {...register("countryCode")}
            />
            <TextInput id="loc-address" label={t("locations:address")} {...register("address")} />
            <TextInput
              id="loc-sgk-isyeri-sicil-no"
              label={t("locations:sgkIsyeriSicilNo")}
              {...register("sgkIsyeriSicilNo")}
            />

            <Text size="sm" fw={500} mt="xs">
              {t("locations:geofenceTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("locations:geofenceHint")}
            </Text>
            <Group grow>
              <Controller
                control={control}
                name="latitude"
                render={({ field }) => (
                  <NumberInput
                    id="loc-latitude"
                    label={t("locations:latitude")}
                    placeholder="39.925533"
                    decimalScale={6}
                    value={field.value ?? ""}
                    onChange={(v) => field.onChange(v === "" ? null : Number(v))}
                    error={errors.latitude ? t("locations:geofenceInvalid") : undefined}
                  />
                )}
              />
              <Controller
                control={control}
                name="longitude"
                render={({ field }) => (
                  <NumberInput
                    id="loc-longitude"
                    label={t("locations:longitude")}
                    placeholder="32.866287"
                    decimalScale={6}
                    value={field.value ?? ""}
                    onChange={(v) => field.onChange(v === "" ? null : Number(v))}
                    error={errors.longitude ? t("locations:geofenceInvalid") : undefined}
                  />
                )}
              />
            </Group>
            <Controller
              control={control}
              name="radiusMeters"
              render={({ field }) => (
                <NumberInput
                  id="loc-radius"
                  label={t("locations:geofenceRadius")}
                  description={t("locations:geofenceRadiusHint")}
                  placeholder="150"
                  min={1}
                  value={field.value ?? ""}
                  onChange={(v) => field.onChange(v === "" ? null : Number(v))}
                />
              )}
            />

            {dialog.location && (
              <div>
                <Text size="sm" fw={500}>
                  {t("locations:selfCheckInCode")}
                </Text>
                <Group gap="xs" mt={4}>
                  <Text size="sm" ff="monospace">
                    {dialog.location.selfCheckInCode}
                  </Text>
                  <CopyButton value={dialog.location.selfCheckInCode}>
                    {({ copied, copy }) => (
                      <Button size="compact-xs" variant="subtle" leftSection={<QrCode size={14} />} onClick={copy}>
                        {copied ? t("locations:copied") : t("locations:copy")}
                      </Button>
                    )}
                  </CopyButton>
                  <Button size="compact-xs" variant="subtle" onClick={() => setQrTarget(dialog.location ?? null)}>
                    {t("locations:showQr")}
                  </Button>
                  <Button size="compact-xs" variant="subtle" color="red" onClick={() => setRegenerateTarget(dialog.location ?? null)}>
                    {t("locations:regenerateCode")}
                  </Button>
                </Group>
                <Text size="xs" c="dimmed" mt={4}>
                  {t("locations:selfCheckInCodeHint")}
                </Text>
              </div>
            )}

            <Group justify="flex-end" gap="sm">
              <Button type="button" variant="default" onClick={closeDialog} disabled={isSaving}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={isSaving}>
                {isSaving ? t("common:saving") : t("common:save")}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      <ConfirmDialog
        open={deleteTarget !== null}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        description={t("locations:deleteConfirmDescription", { name: deleteTarget?.name ?? "" })}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        isLoading={deleteMutation.isPending}
      />

      <LocationQrDialog location={qrTarget} onClose={() => setQrTarget(null)} />

      <ConfirmDialog
        open={regenerateTarget !== null}
        onOpenChange={(open) => !open && setRegenerateTarget(null)}
        title={t("locations:regenerateConfirmTitle")}
        description={t("locations:regenerateConfirmDescription", { name: regenerateTarget?.name ?? "" })}
        confirmLabel={t("locations:regenerateCode")}
        onConfirm={() => regenerateTarget && regenerateMutation.mutate(regenerateTarget.id)}
        isLoading={regenerateMutation.isPending}
      />
    </div>
  );
}
