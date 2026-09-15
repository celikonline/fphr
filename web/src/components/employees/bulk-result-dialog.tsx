import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { CheckCircle2, CircleSlash, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BulkItemOutcome, BulkOperationResult } from "@/types";
import { bulkCodeKey } from "./bulk-actions";

interface BulkResultDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  result: BulkOperationResult | null;
  /** Names of employees seen in the grid; ids of unseen ones (all-matching mode) are shown shortened. */
  nameById: ReadonlyMap<string, string>;
}

const OUTCOME_VARIANT: Record<BulkItemOutcome, "success" | "secondary" | "destructive"> = {
  succeeded: "success",
  skipped: "secondary",
  failed: "destructive",
};

export default function BulkResultDialog({ open, onOpenChange, title, result, nameById }: BulkResultDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const problems = (result?.items ?? []).filter((item) => item.outcome !== "succeeded");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{t("employees:bulk.result.requested", { count: result?.requested ?? 0 })}</DialogDescription>
        </DialogHeader>

        {result && (
          <div className="space-y-4">
            <div className="grid grid-cols-3 gap-3">
              <Summary icon={<CheckCircle2 className="h-5 w-5 text-emerald-600" />} label={t("employees:bulk.outcome.succeeded")} value={result.succeeded} />
              <Summary icon={<CircleSlash className="h-5 w-5 text-muted-foreground" />} label={t("employees:bulk.outcome.skipped")} value={result.skipped} />
              <Summary icon={<XCircle className="h-5 w-5 text-destructive" />} label={t("employees:bulk.outcome.failed")} value={result.failed} />
            </div>

            {problems.length > 0 && (
              <div className="rounded-md border border-border">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t("employees:columns.name")}</TableHead>
                      <TableHead>{t("employees:bulk.result.outcome")}</TableHead>
                      <TableHead>{t("employees:bulk.result.reason")}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {problems.map((item) => (
                      <TableRow key={item.employeeId}>
                        <TableCell className="font-medium">{nameById.get(item.employeeId) ?? `#${item.employeeId.slice(0, 8)}`}</TableCell>
                        <TableCell>
                          <Badge variant={OUTCOME_VARIANT[item.outcome]}>{t(`employees:bulk.outcome.${item.outcome}`)}</Badge>
                        </TableCell>
                        <TableCell className="text-sm">
                          {item.code ? t(bulkCodeKey(item.code), { defaultValue: item.code }) : "—"}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
          </div>
        )}

        <DialogFooter>
          <Button type="button" onClick={() => onOpenChange(false)}>
            {t("common:close")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function Summary({ icon, label, value }: { icon: ReactNode; label: string; value: number }) {
  return (
    <div className="flex items-center gap-3 rounded-md border border-border p-3">
      {icon}
      <div>
        <p className="text-xl font-semibold leading-none">{value}</p>
        <p className="text-xs text-muted-foreground">{label}</p>
      </div>
    </div>
  );
}
