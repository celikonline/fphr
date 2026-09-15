import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  toEmployeeInput,
  updateEmployee,
  type BloodType,
  type ContractType,
  type DisabilityDegree,
  type EducationStatus,
  type EmployeeDetail,
  type EmployeeInput,
  type EmploymentType,
  type Gender,
  type HighestEducationLevel,
  type MaritalStatus,
  type MilitaryStatus,
} from "@/services/employees.service";
import { setUserAccessType, type AccessType } from "@/services/users.service";
import { AccountStatusBadge } from "./employee-badges";
import {
  BLOOD_TYPE_OPTIONS,
  CONTRACT_TYPE_OPTIONS,
  DISABILITY_DEGREE_OPTIONS,
  EDUCATION_STATUS_OPTIONS,
  EMPLOYMENT_TYPE_OPTIONS,
  GENDER_OPTIONS,
  HIGHEST_EDUCATION_LEVEL_OPTIONS,
  MARITAL_STATUS_OPTIONS,
  MILITARY_STATUS_OPTIONS,
} from "./employee-enum-options";
import {
  EmployeeGeneralFields,
  generalValuesFrom,
  generalValuesToInput,
  serverFieldErrors,
  validateGeneral,
  type EmployeeGeneralField,
  type EmployeeGeneralValues,
  type FieldErrors,
} from "./employee-form";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";
import OrgUnitSelect from "./org-unit-select";
import { useOrgUnits } from "./use-org-units";

const ACCESS_TYPES: AccessType[] = ["Employee", "Owner", "Admin"];
const SENSITIVE_PERMISSION = "employee.sensitive.view";
const EMPTY_ENUM_VALUE = "__none__";

interface SectionProps {
  employee: EmployeeDetail;
}

/** A Select for a nullable enum field, with a "not set" option (Radix Select rejects an empty string value). */
function NullableEnumSelect<T extends string>({
  id,
  value,
  onChange,
  options,
  i18nEnum,
  emptyLabel,
  disabled,
}: {
  id: string;
  value: T | null;
  onChange: (value: T | null) => void;
  options: readonly T[];
  i18nEnum: string;
  emptyLabel: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation(["employees"]);
  return (
    <Select value={value ?? EMPTY_ENUM_VALUE} onValueChange={(v) => onChange(v === EMPTY_ENUM_VALUE ? null : (v as T))} disabled={disabled}>
      <SelectTrigger id={id}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={EMPTY_ENUM_VALUE}>{emptyLabel}</SelectItem>
        {options.map((option) => (
          <SelectItem key={option} value={option}>
            {t(`employees:enum.${i18nEnum}.${option}`)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

/** A Select for a non-nullable enum field that always has a value (e.g. "unspecified"). */
function EnumSelect<T extends string>({
  id,
  value,
  onChange,
  options,
  i18nEnum,
  disabled,
}: {
  id: string;
  value: T;
  onChange: (value: T) => void;
  options: readonly T[];
  i18nEnum: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation(["employees"]);
  return (
    <Select value={value} onValueChange={(v) => onChange(v as T)} disabled={disabled}>
      <SelectTrigger id={id}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map((option) => (
          <SelectItem key={option} value={option}>
            {t(`employees:enum.${i18nEnum}.${option}`)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
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

/** "Genel": identity and contact fields, plus employment/contract type. */
export function GeneralSection({ employee }: SectionProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canUpdate = usePermission("employee.employees.update");
  const canViewSensitive = usePermission(SENSITIVE_PERMISSION);
  const [editing, setEditing] = useState(false);
  const [values, setValues] = useState<EmployeeGeneralValues>(() => generalValuesFrom(employee));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [contractType, setContractType] = useState<ContractType | null>(employee.contractType);
  const [employmentType, setEmploymentType] = useState<EmploymentType | null>(employee.employmentType);
  const mutation = useEmployeeUpdate(employee, () => setEditing(false), (error) => setErrors(serverFieldErrors(error)));

  function startEdit() {
    setValues(generalValuesFrom(employee));
    setErrors({});
    setContractType(employee.contractType);
    setEmploymentType(employee.employmentType);
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateGeneral(values, t);
    setErrors(found);
    if (Object.keys(found).length === 0) mutation.mutate({ ...generalValuesToInput(values), contractType, employmentType });
  }

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const disabledFields: ReadonlySet<EmployeeGeneralField> = canViewSensitive ? new Set() : new Set(["nationalId"]);

  return (
    <SectionCard title={t("employees:card.sections.general")} action={canUpdate && !editing ? <EditButton onClick={startEdit} /> : undefined}>
      {editing ? (
        <form onSubmit={submit} className="space-y-4">
          <EmployeeGeneralFields
            idPrefix="card-general"
            values={values}
            errors={errors}
            disabled={mutation.isPending}
            disabledFields={disabledFields}
            onChange={(field, value) => setValues((prev) => ({ ...prev, [field]: value }))}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="card-general-contractType">{t("employees:fields.contractType")}</Label>
              <NullableEnumSelect
                id="card-general-contractType"
                value={contractType}
                onChange={setContractType}
                options={CONTRACT_TYPE_OPTIONS}
                i18nEnum="contractType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
            </div>
            <div>
              <Label htmlFor="card-general-employmentType">{t("employees:fields.employmentType")}</Label>
              <NullableEnumSelect
                id="card-general-employmentType"
                value={employmentType}
                onChange={setEmploymentType}
                options={EMPLOYMENT_TYPE_OPTIONS}
                i18nEnum="employmentType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
            </div>
          </div>
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
          <Field label={t("employees:fields.contractType")}>
            {employee.contractType ? t(`employees:enum.contractType.${employee.contractType}`) : "—"}
          </Field>
          <Field label={t("employees:fields.employmentType")}>
            {employee.employmentType ? t(`employees:enum.employmentType.${employee.employmentType}`) : "—"}
          </Field>
        </dl>
      )}
    </SectionCard>
  );
}

/** "Kişisel bilgiler": demographics, education, military/health (sensitive), contact address. */
export function PersonalSection({ employee }: SectionProps) {
  const { t } = useTranslation(["employees", "common"]);
  const canUpdate = usePermission("employee.employees.update");
  const canViewSensitive = usePermission(SENSITIVE_PERMISSION);
  const [editing, setEditing] = useState(false);

  const [gender, setGender] = useState<Gender>(employee.gender);
  const [maritalStatus, setMaritalStatus] = useState<MaritalStatus>(employee.maritalStatus);
  const [childCount, setChildCount] = useState<string>(employee.childCount?.toString() ?? "");
  const [educationStatus, setEducationStatus] = useState<EducationStatus | null>(employee.educationStatus);
  const [highestEducationLevel, setHighestEducationLevel] = useState<HighestEducationLevel | null>(employee.highestEducationLevel);
  const [militaryStatus, setMilitaryStatus] = useState<MilitaryStatus | null>(employee.militaryStatus);
  const [disabilityDegree, setDisabilityDegree] = useState<DisabilityDegree | null>(employee.disabilityDegree);
  const [bloodType, setBloodType] = useState<BloodType | null>(employee.bloodType);
  const [mobilePhone, setMobilePhone] = useState(employee.mobilePhone ?? "");
  const [homePhone, setHomePhone] = useState(employee.homePhone ?? "");
  const [addressLine, setAddressLine] = useState(employee.addressLine ?? "");
  const [city, setCity] = useState(employee.city ?? "");
  const [postalCode, setPostalCode] = useState(employee.postalCode ?? "");
  const [childCountError, setChildCountError] = useState<string | null>(null);

  const mutation = useEmployeeUpdate(employee, () => setEditing(false));

  function startEdit() {
    setGender(employee.gender);
    setMaritalStatus(employee.maritalStatus);
    setChildCount(employee.childCount?.toString() ?? "");
    setEducationStatus(employee.educationStatus);
    setHighestEducationLevel(employee.highestEducationLevel);
    setMilitaryStatus(employee.militaryStatus);
    setDisabilityDegree(employee.disabilityDegree);
    setBloodType(employee.bloodType);
    setMobilePhone(employee.mobilePhone ?? "");
    setHomePhone(employee.homePhone ?? "");
    setAddressLine(employee.addressLine ?? "");
    setCity(employee.city ?? "");
    setPostalCode(employee.postalCode ?? "");
    setChildCountError(null);
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const trimmedCount = childCount.trim();
    const parsedCount = trimmedCount === "" ? null : Number(trimmedCount);
    if (parsedCount !== null && (!Number.isInteger(parsedCount) || parsedCount < 0)) {
      setChildCountError(t("employees:validation.childCount"));
      return;
    }

    setChildCountError(null);
    mutation.mutate({
      gender,
      maritalStatus,
      childCount: parsedCount,
      educationStatus,
      highestEducationLevel,
      // A caller without employee.sensitive.view never sees these, so an edit here can't clear them either -
      // the server preserves the stored values regardless of what is sent.
      militaryStatus,
      disabilityDegree,
      bloodType,
      mobilePhone: mobilePhone.trim() || null,
      homePhone: homePhone.trim() || null,
      addressLine: addressLine.trim() || null,
      city: city.trim() || null,
      postalCode: postalCode.trim() || null,
    });
  }

  const orDash = (value: string | null) => value ?? "—";

  return (
    <SectionCard title={t("employees:card.sections.personal")} action={canUpdate && !editing ? <EditButton onClick={startEdit} /> : undefined}>
      {!canViewSensitive && (
        <p className="mb-4 text-xs text-muted-foreground">{t("employees:card.sensitiveHidden")}</p>
      )}
      {editing ? (
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="card-personal-gender">{t("employees:fields.gender")}</Label>
              <EnumSelect id="card-personal-gender" value={gender} onChange={setGender} options={GENDER_OPTIONS} i18nEnum="gender" disabled={mutation.isPending} />
            </div>
            <div>
              <Label htmlFor="card-personal-maritalStatus">{t("employees:fields.maritalStatus")}</Label>
              <EnumSelect
                id="card-personal-maritalStatus"
                value={maritalStatus}
                onChange={setMaritalStatus}
                options={MARITAL_STATUS_OPTIONS}
                i18nEnum="maritalStatus"
                disabled={mutation.isPending}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-childCount">{t("employees:fields.childCount")}</Label>
              <Input
                id="card-personal-childCount"
                type="number"
                min={0}
                inputMode="numeric"
                value={childCount}
                disabled={mutation.isPending}
                onChange={(e) => setChildCount(e.target.value)}
              />
              {childCountError && <p className="mt-1 text-xs text-destructive">{childCountError}</p>}
            </div>
            <div>
              <Label htmlFor="card-personal-educationStatus">{t("employees:fields.educationStatus")}</Label>
              <NullableEnumSelect
                id="card-personal-educationStatus"
                value={educationStatus}
                onChange={setEducationStatus}
                options={EDUCATION_STATUS_OPTIONS}
                i18nEnum="educationStatus"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-highestEducationLevel">{t("employees:fields.highestEducationLevel")}</Label>
              <NullableEnumSelect
                id="card-personal-highestEducationLevel"
                value={highestEducationLevel}
                onChange={setHighestEducationLevel}
                options={HIGHEST_EDUCATION_LEVEL_OPTIONS}
                i18nEnum="highestEducationLevel"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-militaryStatus">{t("employees:fields.militaryStatus")}</Label>
              <NullableEnumSelect
                id="card-personal-militaryStatus"
                value={militaryStatus}
                onChange={setMilitaryStatus}
                options={MILITARY_STATUS_OPTIONS}
                i18nEnum="militaryStatus"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-disabilityDegree">{t("employees:fields.disabilityDegree")}</Label>
              <NullableEnumSelect
                id="card-personal-disabilityDegree"
                value={disabilityDegree}
                onChange={setDisabilityDegree}
                options={DISABILITY_DEGREE_OPTIONS}
                i18nEnum="disabilityDegree"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-bloodType">{t("employees:fields.bloodType")}</Label>
              <NullableEnumSelect
                id="card-personal-bloodType"
                value={bloodType}
                onChange={setBloodType}
                options={BLOOD_TYPE_OPTIONS}
                i18nEnum="bloodType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
            </div>
            <div>
              <Label htmlFor="card-personal-mobilePhone">{t("employees:fields.mobilePhone")}</Label>
              <Input id="card-personal-mobilePhone" type="tel" value={mobilePhone} disabled={mutation.isPending} onChange={(e) => setMobilePhone(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="card-personal-homePhone">{t("employees:fields.homePhone")}</Label>
              <Input id="card-personal-homePhone" type="tel" value={homePhone} disabled={mutation.isPending} onChange={(e) => setHomePhone(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="card-personal-addressLine">{t("employees:fields.addressLine")}</Label>
              <Input id="card-personal-addressLine" value={addressLine} disabled={mutation.isPending} onChange={(e) => setAddressLine(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="card-personal-city">{t("employees:fields.city")}</Label>
              <Input id="card-personal-city" value={city} disabled={mutation.isPending} onChange={(e) => setCity(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="card-personal-postalCode">{t("employees:fields.postalCode")}</Label>
              <Input id="card-personal-postalCode" value={postalCode} disabled={mutation.isPending} onChange={(e) => setPostalCode(e.target.value)} />
            </div>
          </div>
          <EditFooter saving={mutation.isPending} onCancel={() => setEditing(false)} />
        </form>
      ) : (
        <dl className="grid gap-4 sm:grid-cols-2">
          <Field label={t("employees:fields.gender")}>{t(`employees:enum.gender.${employee.gender}`)}</Field>
          <Field label={t("employees:fields.maritalStatus")}>{t(`employees:enum.maritalStatus.${employee.maritalStatus}`)}</Field>
          <Field label={t("employees:fields.childCount")}>{employee.childCount ?? "—"}</Field>
          <Field label={t("employees:fields.educationStatus")}>
            {employee.educationStatus ? t(`employees:enum.educationStatus.${employee.educationStatus}`) : "—"}
          </Field>
          <Field label={t("employees:fields.highestEducationLevel")}>
            {employee.highestEducationLevel ? t(`employees:enum.highestEducationLevel.${employee.highestEducationLevel}`) : "—"}
          </Field>
          <Field label={t("employees:fields.militaryStatus")}>
            {employee.militaryStatus ? t(`employees:enum.militaryStatus.${employee.militaryStatus}`) : "—"}
          </Field>
          <Field label={t("employees:fields.disabilityDegree")}>
            {employee.disabilityDegree ? t(`employees:enum.disabilityDegree.${employee.disabilityDegree}`) : "—"}
          </Field>
          <Field label={t("employees:fields.bloodType")}>{employee.bloodType ? t(`employees:enum.bloodType.${employee.bloodType}`) : "—"}</Field>
          <Field label={t("employees:fields.mobilePhone")}>{orDash(employee.mobilePhone)}</Field>
          <Field label={t("employees:fields.homePhone")}>{orDash(employee.homePhone)}</Field>
          <Field label={t("employees:fields.addressLine")}>{orDash(employee.addressLine)}</Field>
          <Field label={t("employees:fields.city")}>{orDash(employee.city)}</Field>
          <Field label={t("employees:fields.postalCode")}>{orDash(employee.postalCode)}</Field>
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
