import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { getApiErrorMessage } from "@/lib/api-error";
import { assignLeaveRuleSet, listLeaveRuleSets, type LeaveRuleSetAssignmentResult } from "@/services/leave.service";
import { useLeaveName } from "./leave-shared";

interface ChangeEmployeeRuleSetDialogProps {
  employeeId: string;
  employeeName: string;
  currentRuleSetId: string;
  onClose: () => void;
  onChanged: (result: LeaveRuleSetAssignmentResult) => void;
}

/** Employee card "Kural setini değiştir": assigns one employee to another rule set (leave.rule_sets.assign). */
export function ChangeEmployeeRuleSetDialog({ employeeId, employeeName, currentRuleSetId, onClose, onChanged }: ChangeEmployeeRuleSetDialogProps) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const [ruleSetId, setRuleSetId] = useState(currentRuleSetId);
  const [error, setError] = useState<string | null>(null);
  const setsQuery = useQuery({ queryKey: ["leave", "rule-sets"], queryFn: listLeaveRuleSets });

  const mutation = useMutation({
    mutationFn: (id: string) => assignLeaveRuleSet(id, [employeeId]),
    onSuccess: (result) => {
      if (result.assignedCount === 0 && result.warnings.length > 0) {
        setError(result.warnings.map((warning) => warning.message).join(" "));
        return;
      }
      onChanged(result);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (ruleSetId === currentRuleSetId) return onClose();
    setError(null);
    mutation.mutate(ruleSetId);
  }

  const busy = mutation.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t("leave:ruleSetChange.title")}</DialogTitle>
          <DialogDescription>{t("leave:ruleSetChange.description", { name: employeeName })}</DialogDescription>
        </DialogHeader>
        {setsQuery.isLoading ? (
          <Skeleton className="h-10 w-full" />
        ) : setsQuery.isError ? (
          <p className="text-sm text-destructive">{getApiErrorMessage(setsQuery.error, t("leave:genericError"))}</p>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <div>
              <Label htmlFor="employee-rule-set">{t("leave:summary.ruleSet")}</Label>
              <Select value={ruleSetId} onValueChange={setRuleSetId} disabled={busy}>
                <SelectTrigger id="employee-rule-set">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(setsQuery.data ?? []).map((set) => (
                    <SelectItem key={set.id} value={set.id}>
                      {leaveName(set.name)}
                      {set.isDefault ? ` (${t("leave:ruleSets.default")})` : ""}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="mt-1 text-xs text-muted-foreground">{t("leave:ruleSetChange.hint")}</p>
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" disabled={busy}>
                {busy ? t("common:saving") : t("common:save")}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
