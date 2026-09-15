import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MapPin, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    setPage(1);
  }, [debouncedSearch]);

  const { data, isLoading } = useQuery({
    queryKey: ["locations", page, debouncedSearch],
    queryFn: () => listLocations({ page, pageSize: 10, q: debouncedSearch || undefined, sort: "name" }),
  });

  const [dialog, setDialog] = useState<DialogState>({ open: false });
  const [deleteTarget, setDeleteTarget] = useState<Location | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", type: "Office", countryCode: "TR", address: "" },
  });

  function openCreate() {
    reset({ name: "", type: "Office", countryCode: "TR", address: "" });
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
    onError: () => {
      toast({ variant: "destructive", title: t("common:error"), description: t("locations:genericError") });
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

      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          className="pl-9"
          placeholder={t("locations:searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : !data || data.items.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("locations:empty")}</p>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("locations:name")}</TableHead>
                    <TableHead>{t("locations:type")}</TableHead>
                    <TableHead>{t("locations:countryCode")}</TableHead>
                    <TableHead>{t("locations:address")}</TableHead>
                    {canManage && <TableHead className="text-right">{t("common:actions")}</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.items.map((location) => (
                    <TableRow key={location.id}>
                      <TableCell className="font-medium">{location.name}</TableCell>
                      <TableCell>{t(`locations:types.${location.type}`)}</TableCell>
                      <TableCell>{location.countryCode}</TableCell>
                      <TableCell>{location.address ?? "—"}</TableCell>
                      {canManage && (
                        <TableCell className="text-right">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            title={t("locations:editLocation")}
                            onClick={() => openEdit(location)}
                          >
                            <Pencil className="h-4 w-4" />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            title={t("locations:deleteLocation")}
                            onClick={() => setDeleteTarget(location)}
                          >
                            <Trash2 className="h-4 w-4" />
                          </Button>
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <PaginationFooter result={data} onPageChange={setPage} />
            </>
          )}
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
              <Select value={watch("type")} onValueChange={(value) => setValue("type", value as LocationType)}>
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
