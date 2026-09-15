import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { createEmployee } from "@/services/employees.service";
import {
  EMPTY_GENERAL_VALUES,
  EmployeeGeneralFields,
  generalValuesToInput,
  serverFieldErrors,
  validateGeneral,
  type EmployeeGeneralValues,
  type FieldErrors,
} from "./employee-form";
import OrgUnitSelect from "./org-unit-select";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";

interface CreateEmployeeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** "Yeni çalışan ekle": only first/last name are required; the card opens after saving. */
export default function CreateEmployeeDialog({ open, onOpenChange }: CreateEmployeeDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [values, setValues] = useState<EmployeeGeneralValues>(EMPTY_GENERAL_VALUES);
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);
  const [manager, setManager] = useState<PickedEmployee | null>(null);
  const [errors, setErrors] = useState<FieldErrors>({});

  function reset() {
    setValues(EMPTY_GENERAL_VALUES);
    setOrgUnitId(null);
    setManager(null);
    setErrors({});
  }

  const mutation = useMutation({
    mutationFn: () => createEmployee({ ...generalValuesToInput(values), orgUnitId, managerEmployeeId: manager?.id ?? null }),
    onSuccess: (employee) => {
      queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:create.success") });
      onOpenChange(false);
      reset();
      navigate(`/app/employees/${employee.id}`);
    },
    onError: (error) => {
      setErrors(serverFieldErrors(error));
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateGeneral(values, t);
    setErrors(found);
    if (Object.keys(found).length === 0) mutation.mutate();
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset();
        onOpenChange(next);
      }}
    >
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{t("employees:create.title")}</DialogTitle>
          <DialogDescription>{t("employees:create.description")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <EmployeeGeneralFields
            idPrefix="new-emp"
            values={values}
            errors={errors}
            disabled={mutation.isPending}
            onChange={(field, value) => setValues((prev) => ({ ...prev, [field]: value }))}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="new-emp-unit">{t("employees:fields.orgUnit")}</Label>
              <OrgUnitSelect id="new-emp-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:noUnit")} />
            </div>
            <div>
              <Label htmlFor="new-emp-manager">{t("employees:fields.manager")}</Label>
              <EmployeePicker id="new-emp-manager" value={manager} onChange={setManager} allowClear />
            </div>
          </div>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                reset();
                onOpenChange(false);
              }}
              disabled={mutation.isPending}
            >
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
