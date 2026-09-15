import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { MapPin, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { ActionIcon, Button, Card, Group, Modal, Select, Stack, Text, TextInput, Title, Tooltip } from "@mantine/core";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import ConfirmDialog from "@/components/confirm-dialog";
import PaginationFooter from "@/components/pagination-footer";
import { toast } from "@/hooks/use-toast";
import { useAnyPermission } from "@/components/permission-guard";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import {
  createLocation,
  deleteLocation,
  listLocations,
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

const schema = z.object({
  name: z.string().min(1),
  type: z.enum(LOCATION_TYPES as [LocationType, ...LocationType[]]),
  countryCode: z
    .string()
    .trim()
    .length(2)
    .transform((v) => v.toUpperCase()),
  address: z.string().optional(),
});

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

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", type: "office", countryCode: "TR", address: "" },
  });

  function openCreate() {
    reset({ name: "", type: "office", countryCode: "TR", address: "" });
    setDialog({ open: true });
  }

  function openEdit(location: Location) {
    reset({
      name: location.name,
      type: location.type,
      countryCode: location.countryCode,
      address: location.address ?? "",
    });
    setDialog({ open: true, location });
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["locations"] });

  const createMutation = useMutation({
    mutationFn: createLocation,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:createSuccess") });
      setDialog({ open: false });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("locations:genericError") }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...input }: { id: string } & FormValues) => updateLocation(id, input),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("locations:updateSuccess") });
      setDialog({ open: false });
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
      meta: { label: t("locations:name"), cellClassName: "font-medium" },
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
                <Tooltip label={t("locations:editLocation")}>
                  <ActionIcon
                    type="button"
                    variant="subtle"
                    color="gray"
                    aria-label={t("locations:editLocation")}
                    onClick={() => openEdit(row.original)}
                  >
                    <Pencil size={16} />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label={t("locations:deleteLocation")}>
                  <ActionIcon
                    type="button"
                    variant="subtle"
                    color="red"
                    aria-label={t("locations:deleteLocation")}
                    onClick={() => setDeleteTarget(row.original)}
                  >
                    <Trash2 size={16} />
                  </ActionIcon>
                </Tooltip>
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
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
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
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("locations:empty")} />
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
    </div>
  );
}
