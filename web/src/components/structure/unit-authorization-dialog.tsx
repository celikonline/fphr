import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Info, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import ConfirmDialog from "@/components/confirm-dialog";
import SearchPicker, { type PickerOption } from "@/components/structure/search-picker";
import { structureErrorMessage } from "@/components/structure/errors";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import {
  grantUnitAuthorization,
  listUnitAuthorizations,
  revokeUnitAuthorization,
  searchUnitAuthorizationCandidates,
  type OrgUnit,
  type UnitAuthorization,
} from "@/services/structure.service";

interface UnitAuthorizationDialogProps {
  unit: OrgUnit;
  onClose: () => void;
}

interface EditorState {
  person: PickerOption | null;
  canViewSalaryAndPayments: boolean;
  canViewUnitReports: boolean;
  /** Set when editing an existing grant (person is fixed). */
  existing?: UnitAuthorization;
}

const personLabel = (grant: UnitAuthorization) => grant.userFullName || grant.userEmail || grant.userId;

/** "Birim yetkilendirme": grants a person access to a unit and all of its sub-units. */
export default function UnitAuthorizationDialog({ unit, onClose }: UnitAuthorizationDialogProps) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.unit_authorizations.manage");

  const queryKey = ["identity", "unit-authorizations", unit.id];
  const { data, isLoading, isError } = useQuery({
    queryKey,
    queryFn: () => listUnitAuthorizations(unit.id),
  });
  const grants = (data ?? []).filter((g) => g.isActive);

  const [editor, setEditor] = useState<EditorState | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<UnitAuthorization | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey });

  const saveMutation = useMutation({
    mutationFn: (state: EditorState) =>
      grantUnitAuthorization({
        orgUnitId: unit.id,
        userId: state.existing?.userId ?? state.person!.id,
        canViewSalaryAndPayments: state.canViewSalaryAndPayments,
        canViewUnitReports: state.canViewUnitReports,
      }),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:authorization.saveSuccess") });
      setEditor(null);
      setSubmitted(false);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const removeMutation = useMutation({
    mutationFn: revokeUnitAuthorization,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:authorization.removeSuccess") });
      setRemoveTarget(null);
    },
    onError: (error) => {
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) });
      setRemoveTarget(null);
    },
  });

  const personMissing = editor !== null && !editor.existing && !editor.person;
  const flagMissing = editor !== null && !editor.canViewSalaryAndPayments && !editor.canViewUnitReports;

  function submit() {
    setSubmitted(true);
    if (!editor || personMissing || flagMissing) return;
    saveMutation.mutate(editor);
  }

  return (
    <>
      <Dialog open onOpenChange={(open) => !open && !saveMutation.isPending && onClose()}>
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5" /> {t("structure:authorization.title")}
            </DialogTitle>
            <DialogDescription>{t("structure:authorization.subtitle", { name: unit.name })}</DialogDescription>
          </DialogHeader>

          <Alert>
            <Info className="h-4 w-4" />
            <AlertDescription className="space-y-1 text-xs">
              <p>{t("structure:authorization.explainScope")}</p>
              <p>{t("structure:authorization.explainAbilities")}</p>
              <p>{t("structure:authorization.explainCustomReports")}</p>
            </AlertDescription>
          </Alert>

          {isLoading ? (
            <div className="space-y-2">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : isError ? (
            <p className="text-sm text-destructive">{t("structure:errors.generic")}</p>
          ) : grants.length === 0 ? (
            <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">
              {t("structure:authorization.empty")}
            </p>
          ) : (
            <ScrollArea className="max-h-72 rounded-md border [&>[data-radix-scroll-area-viewport]]:max-h-72 [&>[data-radix-scroll-area-viewport]>div]:block!">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("structure:authorization.person")}</TableHead>
                    <TableHead>{t("structure:authorization.permissions")}</TableHead>
                    {canManage && <TableHead className="w-24 text-right">{t("common:actions")}</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {grants.map((grant) => (
                    <TableRow key={grant.id}>
                      <TableCell>
                        <div className="font-medium">{personLabel(grant)}</div>
                        {grant.userEmail && grant.userFullName && (
                          <div className="text-xs text-muted-foreground">{grant.userEmail}</div>
                        )}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          <Badge variant="outline">{t("structure:authorization.base")}</Badge>
                          {grant.canViewSalaryAndPayments && (
                            <Badge variant="secondary">{t("structure:authorization.salaryShort")}</Badge>
                          )}
                          {grant.canViewUnitReports && (
                            <Badge variant="secondary">{t("structure:authorization.reportsShort")}</Badge>
                          )}
                        </div>
                      </TableCell>
                      {canManage && (
                        <TableCell className="text-right">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            title={t("common:edit")}
                            onClick={() => {
                              setSubmitted(false);
                              setEditor({
                                person: { id: grant.userId, label: personLabel(grant) },
                                canViewSalaryAndPayments: grant.canViewSalaryAndPayments,
                                canViewUnitReports: grant.canViewUnitReports,
                                existing: grant,
                              });
                            }}
                          >
                            <Pencil className="h-4 w-4" />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="text-destructive hover:text-destructive"
                            title={t("structure:authorization.remove")}
                            onClick={() => setRemoveTarget(grant)}
                          >
                            <Trash2 className="h-4 w-4" />
                          </Button>
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </ScrollArea>
          )}

          {canManage && editor && (
            <div className="space-y-4 rounded-md border bg-muted/30 p-4">
              <div>
                <Label htmlFor="auth-person">{t("structure:authorization.person")}</Label>
                <SearchPicker
                  id="auth-person"
                  queryKey={["identity", "unit-authorization-candidates"]}
                  search={async (q) =>
                    (await searchUnitAuthorizationCandidates(q)).map((c) => ({
                      id: c.userId,
                      label: c.fullName,
                      description: c.email,
                    }))
                  }
                  value={editor.person}
                  onChange={(person) => setEditor((prev) => (prev ? { ...prev, person } : prev))}
                  placeholder={t("structure:authorization.selectPerson")}
                  disabled={!!editor.existing}
                  allowClear={!editor.existing}
                />
                {submitted && personMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
              </div>
              <div className="flex items-start justify-between gap-4">
                <Label htmlFor="auth-salary" className="font-normal leading-snug">
                  {t("structure:authorization.canViewSalary")}
                </Label>
                <Switch
                  id="auth-salary"
                  checked={editor.canViewSalaryAndPayments}
                  onCheckedChange={(checked) =>
                    setEditor((prev) => (prev ? { ...prev, canViewSalaryAndPayments: checked } : prev))
                  }
                />
              </div>
              <div className="flex items-start justify-between gap-4">
                <Label htmlFor="auth-reports" className="font-normal leading-snug">
                  {t("structure:authorization.canViewReports")}
                </Label>
                <Switch
                  id="auth-reports"
                  checked={editor.canViewUnitReports}
                  onCheckedChange={(checked) => setEditor((prev) => (prev ? { ...prev, canViewUnitReports: checked } : prev))}
                />
              </div>
              {submitted && flagMissing && (
                <p className="text-xs text-destructive">{t("structure:authorization.flagRequired")}</p>
              )}
              <div className="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setEditor(null)}
                  disabled={saveMutation.isPending}
                >
                  {t("common:cancel")}
                </Button>
                <Button type="button" onClick={submit} disabled={saveMutation.isPending}>
                  {saveMutation.isPending ? t("common:saving") : t("common:save")}
                </Button>
              </div>
            </div>
          )}

          <DialogFooter className="sm:justify-between">
            {canManage && !editor ? (
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setSubmitted(false);
                  setEditor({ person: null, canViewSalaryAndPayments: false, canViewUnitReports: true });
                }}
              >
                <Plus className="mr-2 h-4 w-4" /> {t("structure:authorization.add")}
              </Button>
            ) : (
              <span />
            )}
            <Button type="button" variant="ghost" onClick={onClose}>
              {t("common:close")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={removeTarget !== null}
        onOpenChange={(open) => !open && setRemoveTarget(null)}
        title={t("structure:authorization.removeTitle")}
        description={t("structure:authorization.removeConfirm", {
          person: removeTarget ? personLabel(removeTarget) : "",
          unit: unit.name,
        })}
        confirmLabel={t("structure:authorization.remove")}
        onConfirm={() => removeTarget && removeMutation.mutate(removeTarget.id)}
        isLoading={removeMutation.isPending}
      />
    </>
  );
}
