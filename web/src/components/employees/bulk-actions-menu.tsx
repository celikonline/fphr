import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { ChevronDown, ListChecks } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { useAuthStore } from "@/store/auth.store";
import type { BulkOperationResult, EmployeeSelection } from "@/types";
import { BULK_ACTIONS, type BulkActionDefinition, type BulkActionInput } from "./bulk-actions";
import BulkResultDialog from "./bulk-result-dialog";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";

interface BulkActionsMenuProps {
  selection: EmployeeSelection | null;
  selectedCount: number;
  nameById: ReadonlyMap<string, string>;
  /** Called after a run finishes (grid refresh + selection reset). */
  onCompleted: () => void;
}

/** "Toplu işlemler → İşlem seç": renders the registry, collects input/confirmation, runs and summarizes the outcome. */
export default function BulkActionsMenu({ selection, selectedCount, nameById, onCompleted }: BulkActionsMenuProps) {
  const { t } = useTranslation(["employees", "common"]);
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const actions = BULK_ACTIONS.filter((action) => hasPermission(action.permission));

  const [pending, setPending] = useState<BulkActionDefinition | null>(null);
  const [manager, setManager] = useState<PickedEmployee | null>(null);
  const [result, setResult] = useState<{ action: BulkActionDefinition; value: BulkOperationResult } | null>(null);
  const [resultNames, setResultNames] = useState<ReadonlyMap<string, string>>(new Map());

  const mutation = useMutation({
    mutationFn: ({ action, input }: { action: BulkActionDefinition; input: BulkActionInput }) => action.run!(selection!, input),
    onSuccess: (value, { action }) => {
      setResultNames(new Map(nameById));
      setPending(null);
      setManager(null);
      setResult({ action, value });
      onCompleted();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) }),
  });

  if (actions.length === 0) return null;

  const disabled = !selection || selectedCount === 0;

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="outline" disabled={disabled}>
            <ListChecks className="mr-2 h-4 w-4" />
            {t("employees:bulk.menu")}
            <ChevronDown className="ml-2 h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="w-64">
          <DropdownMenuLabel>{t("employees:bulk.chooseAction")}</DropdownMenuLabel>
          <DropdownMenuSeparator />
          {actions.map((action) => (
            <DropdownMenuItem
              key={action.code}
              disabled={action.phase !== undefined || !action.run}
              onSelect={() => setPending(action)}
              className="justify-between"
            >
              <span>{t(`employees:${action.labelKey}`)}</span>
              {action.phase !== undefined && <Badge variant="outline">{t("employees:phase", { phase: action.phase })}</Badge>}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>

      <ConfirmDialog
        open={pending !== null && pending.input === undefined}
        onOpenChange={(open) => !open && setPending(null)}
        title={pending ? t(`employees:${pending.labelKey}`) : undefined}
        description={t(`employees:bulk.confirm.${pending?.code ?? "default"}`, {
          count: selectedCount,
          defaultValue: t("employees:bulk.confirm.default", { count: selectedCount }),
        })}
        confirmLabel={t("employees:bulk.run")}
        confirmVariant="default"
        isLoading={mutation.isPending}
        onConfirm={() => pending && mutation.mutate({ action: pending, input: {} })}
      />

      <Dialog
        open={pending?.input === "manager"}
        onOpenChange={(open) => {
          if (!open) {
            setPending(null);
            setManager(null);
          }
        }}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{t("employees:bulk.actions.assignManager")}</DialogTitle>
            <DialogDescription>{t("employees:bulk.assignManager.description", { count: selectedCount })}</DialogDescription>
          </DialogHeader>
          <div>
            <Label htmlFor="bulk-manager">{t("employees:fields.manager")}</Label>
            <EmployeePicker id="bulk-manager" value={manager} onChange={setManager} />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setPending(null)} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={!manager || mutation.isPending}
              onClick={() => pending && manager && mutation.mutate({ action: pending, input: { managerEmployeeId: manager.id } })}
            >
              {t("employees:bulk.continue")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <BulkResultDialog
        open={result !== null}
        onOpenChange={(open) => !open && setResult(null)}
        title={result ? t(`employees:${result.action.labelKey}`) : ""}
        result={result?.value ?? null}
        nameById={resultNames}
      />
    </>
  );
}
