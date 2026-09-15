import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Network, Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { usePermission } from "@/components/permission-guard";
import {
  createOrgUnit,
  deleteOrgUnit,
  groupByParent,
  listOrgUnits,
  updateOrgUnit,
  type OrgUnit,
} from "@/services/organization.service";

const ROOT_VALUE = "__root__";

const schema = z.object({
  name: z.string().min(1),
  parentId: z.string(),
});

type FormValues = z.infer<typeof schema>;

/** Every unit id in the subtree rooted at `id` (including itself) - used to prevent re-parenting a unit under its own descendant. */
function descendantIds(units: OrgUnit[], id: string): Set<string> {
  const result = new Set<string>([id]);
  let added = true;
  while (added) {
    added = false;
    for (const unit of units) {
      if (unit.parentId && result.has(unit.parentId) && !result.has(unit.id)) {
        result.add(unit.id);
        added = true;
      }
    }
  }
  return result;
}

interface DialogState {
  open: boolean;
  mode: "create" | "edit";
  parentId: string | null;
  unit?: OrgUnit;
}

export default function OrganizationPage() {
  const { t } = useTranslation(["organization", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("organization.units.manage");

  const { data: units, isLoading } = useQuery({ queryKey: ["org-units"], queryFn: listOrgUnits });

  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  useEffect(() => {
    if (units && units.length > 0) {
      setExpanded((prev) => (prev.size > 0 ? prev : new Set(units.filter((u) => u.parentId === null).map((u) => u.id))));
    }
  }, [units]);

  const [dialog, setDialog] = useState<DialogState>({ open: false, mode: "create", parentId: null });
  const [deleteTarget, setDeleteTarget] = useState<OrgUnit | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", parentId: ROOT_VALUE },
  });

  function openCreate(parentId: string | null) {
    reset({ name: "", parentId: parentId ?? ROOT_VALUE });
    setDialog({ open: true, mode: "create", parentId });
  }

  function openEdit(unit: OrgUnit) {
    reset({ name: unit.name, parentId: unit.parentId ?? ROOT_VALUE });
    setDialog({ open: true, mode: "edit", parentId: unit.parentId, unit });
  }

  const createMutation = useMutation({
    mutationFn: createOrgUnit,
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["org-units"] });
      setExpanded((prev) => {
        const next = new Set(prev);
        if (created.parentId) next.add(created.parentId);
        return next;
      });
      toast({ variant: "success", title: t("common:success"), description: t("organization:createSuccess") });
      setDialog({ open: false, mode: "create", parentId: null });
    },
    onError: () => {
      toast({ variant: "destructive", title: t("common:error"), description: t("organization:genericError") });
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...input }: { id: string; name: string; parentId: string | null }) => updateOrgUnit(id, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["org-units"] });
      toast({ variant: "success", title: t("common:success"), description: t("organization:updateSuccess") });
      setDialog({ open: false, mode: "create", parentId: null });
    },
    onError: () => {
      toast({ variant: "destructive", title: t("common:error"), description: t("organization:genericError") });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: deleteOrgUnit,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["org-units"] });
      toast({ variant: "success", title: t("common:success"), description: t("organization:deleteSuccess") });
      setDeleteTarget(null);
    },
    onError: (error: Error) => {
      const hasChildren = error.message.includes("child units");
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: hasChildren ? t("organization:deleteHasChildrenError") : t("organization:genericError"),
      });
      setDeleteTarget(null);
    },
  });

  function onSubmit(values: FormValues) {
    const parentId = values.parentId === ROOT_VALUE ? null : values.parentId;
    if (dialog.mode === "edit" && dialog.unit) {
      updateMutation.mutate({ id: dialog.unit.id, name: values.name, parentId });
    } else {
      createMutation.mutate({ name: values.name, parentId });
    }
  }

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const childrenMap = units ? groupByParent(units) : new Map<string | null, OrgUnit[]>();
  const roots = childrenMap.get(null) ?? [];
  const excludedIds = dialog.mode === "edit" && dialog.unit ? descendantIds(units ?? [], dialog.unit.id) : new Set<string>();
  const parentOptions = (units ?? []).filter((u) => !excludedIds.has(u.id));
  const isSaving = createMutation.isPending || updateMutation.isPending;

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <Network className="h-6 w-6" /> {t("organization:title")}
          </h1>
          <p className="text-muted-foreground">{t("organization:subtitle")}</p>
        </div>
        {canManage && (
          <Button type="button" onClick={() => openCreate(null)}>
            <Plus className="mr-2 h-4 w-4" /> {t("organization:addRootUnit")}
          </Button>
        )}
      </div>

      <Card>
        <CardContent className="p-2">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : roots.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("organization:empty")}</p>
          ) : (
            <div className="py-2">
              {roots.map((unit) => (
                <OrgUnitNode
                  key={unit.id}
                  unit={unit}
                  depth={0}
                  childrenMap={childrenMap}
                  expanded={expanded}
                  onToggle={toggle}
                  canManage={canManage}
                  onAddChild={openCreate}
                  onEdit={openEdit}
                  onDelete={setDeleteTarget}
                  t={t}
                />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={dialog.open} onOpenChange={(open) => setDialog((prev) => ({ ...prev, open }))}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {dialog.mode === "edit" ? t("organization:dialogEditTitle") : t("organization:dialogCreateTitle")}
            </DialogTitle>
          </DialogHeader>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <div>
              <Label htmlFor="unit-name">{t("organization:name")}</Label>
              <Input id="unit-name" {...register("name")} />
              {errors.name && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
            </div>
            <div>
              <Label htmlFor="unit-parent">{t("organization:parentUnit")}</Label>
              <Select value={watch("parentId")} onValueChange={(value) => setValue("parentId", value)}>
                <SelectTrigger id="unit-parent">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ROOT_VALUE}>{t("organization:noneRoot")}</SelectItem>
                  {parentOptions.map((unit) => (
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
        description={t("organization:deleteConfirmDescription", { name: deleteTarget?.name ?? "" })}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        isLoading={deleteMutation.isPending}
      />
    </div>
  );
}

interface OrgUnitNodeProps {
  unit: OrgUnit;
  depth: number;
  childrenMap: Map<string | null, OrgUnit[]>;
  expanded: Set<string>;
  onToggle: (id: string) => void;
  canManage: boolean;
  onAddChild: (parentId: string) => void;
  onEdit: (unit: OrgUnit) => void;
  onDelete: (unit: OrgUnit) => void;
  t: (key: string) => string;
}

function OrgUnitNode({
  unit,
  depth,
  childrenMap,
  expanded,
  onToggle,
  canManage,
  onAddChild,
  onEdit,
  onDelete,
  t,
}: OrgUnitNodeProps) {
  const kids = childrenMap.get(unit.id) ?? [];
  const isExpanded = expanded.has(unit.id);

  return (
    <div>
      <div
        className="group flex items-center gap-2 rounded-md px-2 py-1.5 hover:bg-accent"
        style={{ paddingLeft: `${depth * 20 + 8}px` }}
      >
        <button
          type="button"
          onClick={() => onToggle(unit.id)}
          disabled={kids.length === 0}
          className="flex h-5 w-5 shrink-0 items-center justify-center text-muted-foreground disabled:opacity-0"
          aria-label={isExpanded ? t("common:close") : t("common:back")}
        >
          {kids.length > 0 && (isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />)}
        </button>
        <span className="truncate text-sm font-medium">{unit.name}</span>
        <Badge variant="secondary" className="shrink-0">
          {unit.employeeCount} {t("organization:employees")}
        </Badge>
        {canManage && (
          <div className="ml-auto flex shrink-0 gap-1 opacity-0 group-hover:opacity-100">
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              title={t("organization:addChild")}
              onClick={() => onAddChild(unit.id)}
            >
              <Plus className="h-3.5 w-3.5" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              title={t("organization:editUnit")}
              onClick={() => onEdit(unit)}
            >
              <Pencil className="h-3.5 w-3.5" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-destructive hover:text-destructive"
              title={t("organization:deleteUnit")}
              onClick={() => onDelete(unit)}
            >
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        )}
      </div>
      {isExpanded &&
        kids.map((child) => (
          <OrgUnitNode
            key={child.id}
            unit={child}
            depth={depth + 1}
            childrenMap={childrenMap}
            expanded={expanded}
            onToggle={onToggle}
            canManage={canManage}
            onAddChild={onAddChild}
            onEdit={onEdit}
            onDelete={onDelete}
            t={t}
          />
        ))}
    </div>
  );
}
