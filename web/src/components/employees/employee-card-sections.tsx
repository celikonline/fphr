import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import { toEmployeeInput, updateEmployee, type EmployeeDetail, type EmployeeInput } from "@/services/employees.service";
import { setUserAccessType, type AccessType } from "@/services/users.service";
import { AccountStatusBadge } from "./employee-badges";
import {
  EmployeeGeneralFields,
  generalValuesFrom,
  generalValuesToInput,
  serverFieldErrors,
  validateGeneral,
  type EmployeeGeneralValues,
  type FieldErrors,
} from "./employee-form";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";
import OrgUnitSelect from "./org-unit-select";
import { useOrgUnits } from "./use-org-units";

const ACCESS_TYPES: AccessType[] = ["Employee", "Owner", "Admin"];

interface SectionProps {
  employee: EmployeeDetail;
}

function useEmployeeUpdate(employee: EmployeeDetail, onSaved: () => void, onError?: (error: unknown) => void) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (overrides: Partial<EmployeeInput>) => updateEmployee(employee.id, toEmployeeInput(employee, overrides)),
    onSuccess: (updated) => {
      queryClient.setQueryData(["employees", "detail", employee.id], updated);
      queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:card.saved") });
      onSaved();
    },
    onError: (error) => {
      onError?.(error);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });
}

function SectionCard({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-lg">{title}</CardTitle>
        {action}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  );
}

function EditButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation(["common"]);
  return (
    <Button type="button" variant="ghost" size="sm" onClick={onClick}>
      <Pencil className="mr-2 h-4 w-4" /> {t("common:edit")}
    </Button>
  );
}

function EditFooter({ saving, onCancel }: { saving: boolean; onCancel: () => void }) {
  const { t } = useTranslation(["common"]);
  return (
    <div className="flex justify-end gap-2">
      <Button type="button" variant="outline" onClick={onCancel} disabled={saving}>
        {t("common:cancel")}
      </Button>
      <Button type="submit" disabled={saving}>
        {saving ? t("common:saving") : t("common:save")}
      </Button>
    </div>
  );
}

/** "Genel": identity and contact fields. */
export function GeneralSection({ employee }: SectionProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canUpdate = usePermission("employee.employees.update");
  const [editing, setEditing] = useState(false);
  const [values, setValues] = useState<EmployeeGeneralValues>(() => generalValuesFrom(employee));
  const [errors, setErrors] = useState<FieldErrors>({});
  const mutation = useEmployeeUpdate(employee, () => setEditing(false), (error) => setErrors(serverFieldErrors(error)));

  function startEdit() {
    setValues(generalValuesFrom(employee));
    setErrors({});
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateGeneral(values, t);
    setErrors(found);
    if (Object.keys(found).length === 0) mutation.mutate(generalValuesToInput(values));
  }

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <SectionCard title={t("employees:card.sections.general")} action={canUpdate && !editing ? <EditButton onClick={startEdit} /> : undefined}>
      {editing ? (
        <form onSubmit={submit} className="space-y-4">
          <EmployeeGeneralFields
            idPrefix="card-general"
            values={values}
            errors={errors}
            disabled={mutation.isPending}
            onChange={(field, value) => setValues((prev) => ({ ...prev, [field]: value }))}
          />
          <EditFooter saving={mutation.isPending} onCancel={() => setEditing(false)} />
        </form>
      ) : (
        <dl className="grid gap-4 sm:grid-cols-2">
          <Field label={t("employees:fields.firstName")}>{employee.firstName}</Field>
          <Field label={t("employees:fields.lastName")}>{employee.lastName}</Field>
          <Field label={t("employees:fields.nationalId")}>{employee.nationalId ?? "—"}</Field>
          <Field label={t("employees:fields.workEmail")}>{employee.workEmail ?? "—"}</Field>
          <Field label={t("employees:fields.personalEmail")}>{employee.personalEmail ?? "—"}</Field>
          <Field label={t("employees:fields.workPhone")}>{employee.workPhone ?? "—"}</Field>
          <Field label={t("employees:fields.hireDate")}>{date(employee.hireDate)}</Field>
          <Field label={t("employees:fields.birthDate")}>{date(employee.birthDate)}</Field>
        </dl>
      )}
    </SectionCard>
  );
}

/** "Kariyer": org unit and direct manager. */
export function CareerSection({ employee }: SectionProps) {
  const { t } = useTranslation(["employees", "common"]);
  const canUpdate = usePermission("employee.employees.update");
  const { nameById } = useOrgUnits();
  const [editing, setEditing] = useState(false);
  const [orgUnitId, setOrgUnitId] = useState<string | null>(employee.orgUnitId);
  const [manager, setManager] = useState<PickedEmployee | null>(null);
  const mutation = useEmployeeUpdate(employee, () => setEditing(false));

  function startEdit() {
    setOrgUnitId(employee.orgUnitId);
    setManager(
      employee.managerEmployeeId
        ? { id: employee.managerEmployeeId, displayName: employee.managerDisplayName ?? t("employees:unknownEmployee") }
        : null
    );
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    mutation.mutate({ orgUnitId, managerEmployeeId: manager?.id ?? null });
  }

  return (
    <SectionCard title={t("employees:card.sections.career")} action={canUpdate && !editing ? <EditButton onClick={startEdit} /> : undefined}>
      {editing ? (
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="card-unit">{t("employees:fields.orgUnit")}</Label>
              <OrgUnitSelect id="card-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:noUnit")} disabled={mutation.isPending} />
            </div>
            <div>
              <Label htmlFor="card-manager">{t("employees:fields.manager")}</Label>
              <EmployeePicker
                id="card-manager"
                value={manager}
                onChange={setManager}
                excludeId={employee.id}
                allowClear
                disabled={mutation.isPending}
              />
            </div>
          </div>
          <EditFooter saving={mutation.isPending} onCancel={() => setEditing(false)} />
        </form>
      ) : (
        <dl className="grid gap-4 sm:grid-cols-2">
          <Field label={t("employees:fields.orgUnit")}>{(employee.orgUnitId && nameById.get(employee.orgUnitId)) || "—"}</Field>
          <Field label={t("employees:fields.manager")}>
            {employee.managerEmployeeId ? (
              <Link className="text-primary hover:underline" to={`/app/employees/${employee.managerEmployeeId}`}>
                {employee.managerDisplayName ?? t("employees:unknownEmployee")}
              </Link>
            ) : (
              "—"
            )}
          </Field>
        </dl>
      )}
    </SectionCard>
  );
}

/** "Hesap": login account status, last invitation and the Çalışan/Hesap sahibi/Yönetici access type (roadmap #2). */
export function AccountSection({ employee }: SectionProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const canManageUsers = usePermission("identity.users.manage");
  const [accessType, setAccessType] = useState<AccessType | null>(employee.accessType);
  const [accessError, setAccessError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: AccessType) => setUserAccessType(employee.userId!, value),
    onSuccess: () => {
      setAccessError(null);
      queryClient.invalidateQueries({ queryKey: ["employees", "detail", employee.id] });
      queryClient.invalidateQueries({ queryKey: ["users-all"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:card.accessTypeSaved") });
    },
    onError: (error) => {
      const message = getApiErrorMessage(error, t("employees:genericError"));
      setAccessError(message);
      setAccessType(employee.accessType);
      toast({ variant: "destructive", title: t("common:error"), description: message });
    },
  });

  const showAccessTypeEditor = Boolean(employee.userId) && canManageUsers;
  const dirty = accessType !== null && accessType !== employee.accessType;

  return (
    <SectionCard title={t("employees:card.sections.account")}>
      <div className="space-y-5">
        <dl className="grid gap-4 sm:grid-cols-2">
          <Field label={t("employees:fields.accountStatus")}>
            <AccountStatusBadge status={employee.accountStatus} />
          </Field>
          <Field label={t("employees:fields.lastInvitationSentAt")}>{formatDateTime(employee.lastInvitationSentAt, i18n.language)}</Field>
          {!showAccessTypeEditor && employee.userId && (
            <Field label={t("employees:fields.accessType")}>
              {employee.accessType ? t(`employees:accessType.${employee.accessType}`) : "—"}
            </Field>
          )}
        </dl>

        {!employee.userId && <p className="text-sm text-muted-foreground">{t("employees:card.noAccountHint")}</p>}

        {showAccessTypeEditor && (
          <div className="space-y-3 rounded-md border border-border p-4">
            <div>
              <p className="text-sm font-medium">{t("employees:fields.accessType")}</p>
              <p className="text-xs text-muted-foreground">{t("employees:card.accessTypeHint")}</p>
            </div>
            <RadioGroup
              value={accessType ?? ""}
              onValueChange={(value) => {
                setAccessError(null);
                setAccessType(value as AccessType);
              }}
              className="flex flex-wrap gap-4"
              disabled={mutation.isPending}
            >
              {ACCESS_TYPES.map((type) => (
                <div key={type} className="flex items-center gap-2">
                  <RadioGroupItem id={`access-${type}`} value={type} />
                  <Label htmlFor={`access-${type}`} className="font-normal">
                    {t(`employees:accessType.${type}`)}
                  </Label>
                </div>
              ))}
            </RadioGroup>
            {accessError && <p className="text-sm text-destructive">{accessError}</p>}
            <div className="flex justify-end">
              <Button type="button" size="sm" disabled={!dirty || mutation.isPending} onClick={() => accessType && mutation.mutate(accessType)}>
                {mutation.isPending ? t("common:saving") : t("common:save")}
              </Button>
            </div>
          </div>
        )}
      </div>
    </SectionCard>
  );
}
