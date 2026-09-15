import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Layers, Lock, MoreHorizontal, Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { structureErrorMessage } from "@/components/structure/errors";
import {
  COMPANY_TYPE_CODE,
  activateUnitType,
  createUnitType,
  deleteUnitType,
  renameUnitType,
  reorderUnitTypes,
  type OrgUnitType,
} from "@/services/structure.service";

export const UNIT_TYPES_QUERY_KEY = ["organization", "unit-types"] as const;

interface LevelsPanelProps {
  /** All levels (active and inactive). */
  types: OrgUnitType[];
  selectedTypeId: string | null;
  onSelect: (typeId: string) => void;
  canManage: boolean;
  unitCounts: Record<string, number>;
}

type AddState = { insertAfter: OrgUnitType } | null;

export default function LevelsPanel({ types, selectedTypeId, onSelect, canManage, unitCounts }: LevelsPanelProps) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();

  const active = types.filter((x) => x.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
  const inactive = types.filter((x) => !x.isActive).sort((a, b) => a.sortOrder - b.sortOrder);

  const [addState, setAddState] = useState<AddState>(null);
  const [renameTarget, setRenameTarget] = useState<OrgUnitType | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<OrgUnitType | null>(null);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: UNIT_TYPES_QUERY_KEY });
    queryClient.invalidateQueries({ queryKey: ["organization", "units"] });
  };
  const onError = (error: unknown, statusKeys?: Partial<Record<number, string>>) =>
    toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t, undefined, statusKeys) });

  const reorderMutation = useMutation({
    mutationFn: reorderUnitTypes,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.reorderSuccess") });
    },
    onError: (error) => onError(error),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteUnitType,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.deleteSuccess") });
      setDeleteTarget(null);
    },
    onError: (error) => {
      onError(error, { 409: "structure:errors.levelInUse", 422: "structure:errors.levelSystemImmutable" });
      setDeleteTarget(null);
    },
  });

  function move(level: OrgUnitType, direction: -1 | 1) {
    const ids = active.map((x) => x.id);
    const index = ids.indexOf(level.id);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    reorderMutation.mutate(ids);
  }

  const isCompany = (level: OrgUnitType) => level.code === COMPANY_TYPE_CODE;

  return (
    <Card className="h-fit">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Layers className="h-4 w-4" /> {t("structure:levels.title")}
        </CardTitle>
        <p className="text-xs text-muted-foreground">{t("structure:levels.subtitle")}</p>
      </CardHeader>
      <CardContent className="px-3 pb-4">
        <ol className="space-y-0">
          {active.map((level, index) => {
            const selected = level.id === selectedTypeId;
            const locked = isCompany(level);
            // Company is pinned first: it can't move and nothing can move above it.
            const canMoveUp = !locked && index > 1;
            const canMoveDown = !locked && index < active.length - 1;
            return (
              <li key={level.id}>
                <div
                  className={cn(
                    "group flex items-center gap-2 rounded-md border px-3 py-2 transition-colors",
                    selected ? "border-primary bg-primary/5" : "border-transparent hover:bg-muted",
                  )}
                >
                  <button
                    type="button"
                    className="flex min-w-0 flex-1 items-center gap-2 text-left"
                    onClick={() => onSelect(level.id)}
                  >
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-medium">
                      {index + 1}
                    </span>
                    <span className={cn("truncate text-sm", selected && "font-semibold")}>{level.name}</span>
                    {locked && <Lock className="h-3 w-3 shrink-0 text-muted-foreground" aria-label={t("structure:levels.system")} />}
                    <span className="ml-auto shrink-0 text-xs text-muted-foreground">{unitCounts[level.id] ?? 0}</span>
                  </button>
                  {canManage && (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button type="button" variant="ghost" size="icon" className="h-7 w-7" title={t("common:actions")}>
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onSelect={() => setRenameTarget(level)}>
                          <Pencil className="mr-2 h-4 w-4" /> {t("structure:levels.rename")}
                        </DropdownMenuItem>
                        <DropdownMenuItem disabled={!canMoveUp || reorderMutation.isPending} onSelect={() => move(level, -1)}>
                          <ArrowUp className="mr-2 h-4 w-4" /> {t("structure:levels.moveUp")}
                        </DropdownMenuItem>
                        <DropdownMenuItem disabled={!canMoveDown || reorderMutation.isPending} onSelect={() => move(level, 1)}>
                          <ArrowDown className="mr-2 h-4 w-4" /> {t("structure:levels.moveDown")}
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          disabled={locked}
                          className="text-destructive focus:text-destructive"
                          onSelect={() => setDeleteTarget(level)}
                        >
                          <Trash2 className="mr-2 h-4 w-4" /> {t("common:delete")}
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                </div>
                {canManage && (
                  <div className="flex items-center gap-2 py-0.5 pl-6">
                    <span className="h-px flex-1 bg-border" />
                    <Button
                      type="button"
                      variant="outline"
                      size="icon"
                      className="h-6 w-6 rounded-full"
                      title={t("structure:levels.addAfter", { name: level.name })}
                      onClick={() => setAddState({ insertAfter: level })}
                    >
                      <Plus className="h-3 w-3" />
                    </Button>
                    <span className="h-px flex-1 bg-border" />
                  </div>
                )}
              </li>
            );
          })}
        </ol>
      </CardContent>

      {addState && (
        <AddLevelDialog
          insertAfter={addState.insertAfter}
          inactive={inactive}
          onClose={() => setAddState(null)}
          onDone={(created) => {
            invalidate();
            onSelect(created.id);
            setAddState(null);
          }}
        />
      )}

      {renameTarget && (
        <RenameLevelDialog level={renameTarget} onClose={() => setRenameTarget(null)} onDone={invalidate} />
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={t("structure:levels.deleteTitle")}
        description={t("structure:levels.deleteConfirm", { name: deleteTarget?.name ?? "" })}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        isLoading={deleteMutation.isPending}
      />
    </Card>
  );
}

function AddLevelDialog({
  insertAfter,
  inactive,
  onClose,
  onDone,
}: {
  insertAfter: OrgUnitType;
  inactive: OrgUnitType[];
  onClose: () => void;
  onDone: (created: OrgUnitType) => void;
}) {
  const { t } = useTranslation(["structure", "common"]);
  const [mode, setMode] = useState<"new" | "activate">("new");
  const [name, setName] = useState("");
  const [activateId, setActivateId] = useState<string>(inactive[0]?.id ?? "");

  const mutation = useMutation({
    mutationFn: () =>
      mode === "new"
        ? createUnitType({ name: name.trim(), insertAfterTypeId: insertAfter.id })
        : activateUnitType(activateId, insertAfter.id),
    onSuccess: (created) => {
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.addSuccess") });
      onDone(created);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const valid = mode === "new" ? name.trim().length > 0 : activateId !== "";

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("structure:levels.addTitle")}</DialogTitle>
          <DialogDescription>{t("structure:levels.addDescription", { name: insertAfter.name })}</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          {inactive.length > 0 && (
            <RadioGroup value={mode} onValueChange={(v) => setMode(v as "new" | "activate")} className="gap-2">
              <div className="flex items-center gap-2">
                <RadioGroupItem id="level-mode-activate" value="activate" />
                <Label htmlFor="level-mode-activate" className="font-normal">
                  {t("structure:levels.modeActivate")}
                </Label>
              </div>
              <div className="flex items-center gap-2">
                <RadioGroupItem id="level-mode-new" value="new" />
                <Label htmlFor="level-mode-new" className="font-normal">
                  {t("structure:levels.modeNew")}
                </Label>
              </div>
            </RadioGroup>
          )}

          {mode === "activate" ? (
            <div className="grid gap-2 sm:grid-cols-2">
              {inactive.map((level) => (
                <button
                  key={level.id}
                  type="button"
                  onClick={() => setActivateId(level.id)}
                  className={cn(
                    "rounded-md border px-3 py-2 text-left text-sm transition-colors",
                    activateId === level.id ? "border-primary bg-primary/5 font-medium" : "hover:bg-muted",
                  )}
                >
                  {level.name}
                </button>
              ))}
            </div>
          ) : (
            <div>
              <Label htmlFor="level-name">{t("structure:levels.name")}</Label>
              <Input
                id="level-name"
                autoFocus
                maxLength={200}
                placeholder={t("structure:levels.namePlaceholder")}
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={!valid || mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RenameLevelDialog({ level, onClose, onDone }: { level: OrgUnitType; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const [name, setName] = useState(level.name);

  const mutation = useMutation({
    mutationFn: () => renameUnitType(level.id, name.trim()),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.renameSuccess") });
      onDone();
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("structure:levels.renameTitle")}</DialogTitle>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="level-rename">{t("structure:levels.name")}</Label>
            <Input id="level-rename" autoFocus maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim() || mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
