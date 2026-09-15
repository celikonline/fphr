import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Briefcase, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
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
import { usePermission } from "@/components/permission-guard";
import {
  createPosition,
  deletePosition,
  listPositions,
  updatePosition,
  type Position,
  type PositionLevel,
} from "@/services/positions.service";
import { listOrgUnits } from "@/services/organization.service";

const NONE_VALUE = "__none__";
const ALL_VALUE = "__all__";
const LEVELS: PositionLevel[] = ["junior", "mid", "senior", "lead", "manager", "director"];

const schema = z.object({
  title: z.string().min(1),
  level: z.enum(["junior", "mid", "senior", "lead", "manager", "director"]),
  orgUnitId: z.string(),
});

type FormValues = z.infer<typeof schema>;

interface DialogState {
  open: boolean;
  position?: Position;
}

export default function PositionsPage() {
  const { t } = useTranslation(["positions", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("organization.positions.manage");

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [orgUnitFilter, setOrgUnitFilter] = useState(ALL_VALUE);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, orgUnitFilter]);

  const { data, isLoading } = useQuery({
    queryKey: ["positions", page, debouncedSearch, orgUnitFilter],
    queryFn: () =>
      listPositions({
        page,
        pageSize: 10,
        q: debouncedSearch || undefined,
        sort: "title",
        orgUnitId: orgUnitFilter === ALL_VALUE ? undefined : orgUnitFilter,
      }),
  });

  const { data: orgUnits } = useQuery({ queryKey: ["org-units"], queryFn: listOrgUnits });
  const orgUnitName = (id: string | null) => orgUnits?.find((u) => u.id === id)?.name ?? "—";

  const [dialog, setDialog] = useState<DialogState>({ open: false });
  const [deleteTarget, setDeleteTarget] = useState<Position | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", level: "mid", orgUnitId: NONE_VALUE },
  });

  function openCreate() {
    reset({ title: "", level: "mid", orgUnitId: NONE_VALUE });
    setDialog({ open: true });
  }

  function openEdit(position: Position) {
    reset({ title: position.title, level: position.level, orgUnitId: position.orgUnitId ?? NONE_VALUE });
    setDialog({ open: true, position });
  }

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["positions"] });

  const createMutation = useMutation({
    mutationFn: createPosition,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("positions:createSuccess") });
      setDialog({ open: false });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("positions:genericError") }),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...input }: { id: string } & FormValues) =>
      updatePosition(id, { ...input, orgUnitId: input.orgUnitId === NONE_VALUE ? null : input.orgUnitId }),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("positions:updateSuccess") });
      setDialog({ open: false });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("positions:genericError") }),
  });

  const deleteMutation = useMutation({
    mutationFn: deletePosition,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("positions:deleteSuccess") });
      setDeleteTarget(null);
    },
    onError: () => {
      toast({ variant: "destructive", title: t("common:error"), description: t("positions:genericError") });
      setDeleteTarget(null);
    },
  });

  function onSubmit(values: FormValues) {
    const orgUnitId = values.orgUnitId === NONE_VALUE ? null : values.orgUnitId;
    if (dialog.position) {
      updateMutation.mutate({ id: dialog.position.id, ...values });
    } else {
      createMutation.mutate({ ...values, orgUnitId });
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending;

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Briefcase className="h-6 w-6" /> {t("positions:title")}
          </h1>
          <p className="text-muted-foreground">{t("positions:subtitle")}</p>
        </div>
        {canManage && (
          <Button type="button" onClick={openCreate}>
            <Plus className="mr-2 h-4 w-4" /> {t("positions:addPosition")}
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder={t("positions:searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Select value={orgUnitFilter} onValueChange={setOrgUnitFilter}>
          <SelectTrigger className="w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_VALUE}>{t("positions:allOrgUnits")}</SelectItem>
            {orgUnits?.map((unit) => (
              <SelectItem key={unit.id} value={unit.id}>
                {unit.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
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
            <p className="p-6 text-center text-sm text-muted-foreground">{t("positions:empty")}</p>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("positions:titleLabel")}</TableHead>
                    <TableHead>{t("positions:level")}</TableHead>
                    <TableHead>{t("positions:orgUnit")}</TableHead>
                    {canManage && <TableHead className="text-right">{t("common:actions")}</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.items.map((position) => (
                    <TableRow key={position.id}>
                      <TableCell className="font-medium">{position.title}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{t(`positions:levels.${position.level}`)}</Badge>
                      </TableCell>
                      <TableCell>{orgUnitName(position.orgUnitId)}</TableCell>
                      {canManage && (
                        <TableCell className="text-right">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            title={t("positions:editPosition")}
                            onClick={() => openEdit(position)}
                          >
                            <Pencil className="h-4 w-4" />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            title={t("positions:deletePosition")}
                            onClick={() => setDeleteTarget(position)}
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
            <DialogTitle>{dialog.position ? t("positions:dialogEditTitle") : t("positions:dialogCreateTitle")}</DialogTitle>
          </DialogHeader>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <div>
              <Label htmlFor="pos-title">{t("positions:titleLabel")}</Label>
              <Input id="pos-title" {...register("title")} />
              {errors.title && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
            </div>
            <div>
              <Label htmlFor="pos-level">{t("positions:level")}</Label>
              <Select value={watch("level")} onValueChange={(value) => setValue("level", value as PositionLevel)}>
                <SelectTrigger id="pos-level">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {LEVELS.map((level) => (
                    <SelectItem key={level} value={level}>
                      {t(`positions:levels.${level}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="pos-org-unit">{t("positions:orgUnit")}</Label>
              <Select value={watch("orgUnitId")} onValueChange={(value) => setValue("orgUnitId", value)}>
                <SelectTrigger id="pos-org-unit">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE_VALUE}>{t("positions:noneOrgUnit")}</SelectItem>
                  {orgUnits?.map((unit) => (
                    <SelectItem key={unit.id} value={unit.id}>
                      {unit.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
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
        description={t("positions:deleteConfirmDescription", { title: deleteTarget?.title ?? "" })}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        isLoading={deleteMutation.isPending}
      />
    </div>
  );
}
