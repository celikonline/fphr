import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { MapPin, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
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
    setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", type: "office", countryCode: "TR", address: "" },
  });
  const selectedType = useWatch({ control, name: "type" });

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
              <div className="whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  title={t("locations:editLocation")}
                  onClick={() => openEdit(row.original)}
                >
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="text-destructive hover:text-destructive"
                  title={t("locations:deleteLocation")}
                  onClick={() => setDeleteTarget(row.original)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
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

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <MapPin className="h-6 w-6" /> {t("locations:title")}
          </h1>
          <p className="text-muted-foreground">{t("locations:subtitle")}</p>
        </div>
        {canManage && (
          <Button type="button" onClick={openCreate}>
            <Plus className="mr-2 h-4 w-4" /> {t("locations:addLocation")}
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="relative w-full max-w-sm">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder={t("locations:searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <DataTableViewOptions table={table} className="ml-auto" />
      </div>

      <Card>
        <CardContent className="p-0">
          <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("locations:empty")} />
          {data && <PaginationFooter result={data} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={changePageSize} />}
        </CardContent>
      </Card>

      <Dialog open={dialog.open} onOpenChange={(open) => setDialog((prev) => ({ ...prev, open }))}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{dialog.location ? t("locations:dialogEditTitle") : t("locations:dialogCreateTitle")}</DialogTitle>
          </DialogHeader>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <div>
              <Label htmlFor="loc-name">{t("locations:name")}</Label>
              <Input id="loc-name" {...register("name")} />
              {errors.name && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
            </div>
            <div>
              <Label htmlFor="loc-type">{t("locations:type")}</Label>
              <Select value={selectedType}onValueChange={(value) => setValue("type", value as LocationType)}>
                <SelectTrigger id="loc-type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {LOCATION_TYPES.map((type) => (
                    <SelectItem key={type} value={type}>
                      {t(`locations:types.${type}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="loc-country">{t("locations:countryCode")}</Label>
              <Input id="loc-country" maxLength={2} placeholder="TR" {...register("countryCode")} />
              {errors.countryCode && <p className="mt-1 text-xs text-destructive">{t("locations:countryCodeInvalid")}</p>}
            </div>
            <div>
              <Label htmlFor="loc-address">{t("locations:address")}</Label>
              <Input id="loc-address" {...register("address")} />
            </div>
            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => setDialog((prev) => ({ ...prev, open: false }))}
                disabled={isSaving}
              >
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={isSaving}>
                {isSaving ? t("common:saving") : t("common:save")}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

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
