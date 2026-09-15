import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import { Button, Card, Group, Input, NumberInput, Paper, Radio, Select, Stack, Text, TextInput, Title } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
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

const ACCESS_TYPES: AccessType[] = ["Employee", "Owner", "Admin"];
const SENSITIVE_PERMISSION = "employee.sensitive.view";
const EMPTY_ENUM_VALUE = "__none__";

interface SectionProps {
  employee: EmployeeDetail;
}

/** A Select for a nullable enum field, with a visible "not set" option (kept as a sentinel value so it stays pickable). */
function NullableEnumSelect<T extends string>({
  id,
  label,
  value,
  onChange,
  options,
  i18nEnum,
  emptyLabel,
  disabled,
}: {
  id: string;
  label: string;
  value: T | null;
  onChange: (value: T | null) => void;
  options: readonly T[];
  i18nEnum: string;
  emptyLabel: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation(["employees"]);
  return (
    <Select
      id={id}
      label={label}
      data={[
        { value: EMPTY_ENUM_VALUE, label: emptyLabel },
        ...options.map((option) => ({ value: option, label: t(`employees:enum.${i18nEnum}.${option}`) })),
      ]}
      value={value ?? EMPTY_ENUM_VALUE}
      onChange={(v) => onChange(v === null || v === EMPTY_ENUM_VALUE ? null : (v as T))}
      allowDeselect={false}
      disabled={disabled}
    />
  );
}

/** A Select for a non-nullable enum field that always has a value (e.g. "unspecified"). */
function EnumSelect<T extends string>({
  id,
  label,
  value,
  onChange,
  options,
  i18nEnum,
  disabled,
}: {
  id: string;
  label: string;
  value: T;
  onChange: (value: T) => void;
  options: readonly T[];
  i18nEnum: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation(["employees"]);
  return (
    <Select
      id={id}
      label={label}
      data={options.map((option) => ({ value: option, label: t(`employees:enum.${i18nEnum}.${option}`) }))}
      value={value}
      onChange={(v) => v !== null && onChange(v as T)}
      allowDeselect={false}
      disabled={disabled}
    />
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
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Title order={4}>{title}</Title>
        {action}
      </Group>
      {children}
    </Card>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text component="dt" size="xs" c="dimmed">
        {label}
      </Text>
      <Text component="dd" size="sm" mt={2}>
        {children}
      </Text>
    </div>
  );
}

function EditButton({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation(["common"]);
  return (
    <Button type="button" variant="subtle" size="sm" leftSection={<Pencil size={16} />} onClick={onClick}>
      {t("common:edit")}
    </Button>
  );
}

function EditFooter({ saving, onCancel }: { saving: boolean; onCancel: () => void }) {
  const { t } = useTranslation(["common"]);
  return (
    <Group justify="flex-end" gap="sm">
      <Button type="button" variant="default" onClick={onCancel} disabled={saving}>
        {t("common:cancel")}
      </Button>
      <Button type="submit" disabled={saving}>
        {saving ? t("common:saving") : t("common:save")}
      </Button>
    </Group>
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
  const [leaveAccrualStartDate, setLeaveAccrualStartDate] = useState(employee.leaveAccrualStartDate ?? "");
  const mutation = useEmployeeUpdate(employee, () => setEditing(false), (error) => setErrors(serverFieldErrors(error)));

  function startEdit() {
    setValues(generalValuesFrom(employee));
    setErrors({});
    setContractType(employee.contractType);
    setEmploymentType(employee.employmentType);
    setLeaveAccrualStartDate(employee.leaveAccrualStartDate ?? "");
    setEditing(true);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateGeneral(values, t);
    setErrors(found);
    if (Object.keys(found).length === 0)
      mutation.mutate({ ...generalValuesToInput(values), contractType, employmentType, leaveAccrualStartDate: leaveAccrualStartDate || null });
  }

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const disabledFields: ReadonlySet<EmployeeGeneralField> = canViewSensitive ? new Set() : new Set(["nationalId"]);

  return (
    <SectionCard title={t("employees:card.sections.general")} action={canUpdate && !editing ? <EditButton onClick={startEdit} /> : undefined}>
      {editing ? (
        <form onSubmit={submit}>
          <Stack gap="md">
            <EmployeeGeneralFields
              idPrefix="card-general"
              values={values}
              errors={errors}
              disabled={mutation.isPending}
              disabledFields={disabledFields}
              onChange={(field, value) => setValues((prev) => ({ ...prev, [field]: value }))}
            />
            <div className="grid gap-4 sm:grid-cols-2">
              <NullableEnumSelect
                id="card-general-contractType"
                label={t("employees:fields.contractType")}
                value={contractType}
                onChange={setContractType}
                options={CONTRACT_TYPE_OPTIONS}
                i18nEnum="contractType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
              <NullableEnumSelect
                id="card-general-employmentType"
                label={t("employees:fields.employmentType")}
                value={employmentType}
                onChange={setEmploymentType}
                options={EMPLOYMENT_TYPE_OPTIONS}
                i18nEnum="employmentType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
              <Input.Wrapper
                id="card-general-leaveAccrualStartDate"
                label={t("employees:fields.leaveAccrualStartDate")}
                description={t("employees:card.leaveAccrualStartDateHint")}
                inputWrapperOrder={["label", "input", "description"]}
              >
                <DatePicker
                  id="card-general-leaveAccrualStartDate"
                  value={leaveAccrualStartDate}
                  disabled={mutation.isPending}
                  clearable
                  onChange={setLeaveAccrualStartDate}
                />
              </Input.Wrapper>
            </div>
            <EditFooter saving={mutation.isPending} onCancel={() => setEditing(false)} />
          </Stack>
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
          <Field label={t("employees:fields.leaveAccrualStartDate")}>{date(employee.leaveAccrualStartDate)}</Field>
          <Field label={t("employees:fields.positionStartDate")}>{date(employee.positionStartDate)}</Field>
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
        <Text size="xs" c="dimmed" mb="md">
          {t("employees:card.sensitiveHidden")}
        </Text>
      )}
      {editing ? (
        <form onSubmit={submit}>
          <Stack gap="md">
            <div className="grid gap-4 sm:grid-cols-2">
              <EnumSelect
                id="card-personal-gender"
                label={t("employees:fields.gender")}
                value={gender}
                onChange={setGender}
                options={GENDER_OPTIONS}
                i18nEnum="gender"
                disabled={mutation.isPending}
              />
              <EnumSelect
                id="card-personal-maritalStatus"
                label={t("employees:fields.maritalStatus")}
                value={maritalStatus}
                onChange={setMaritalStatus}
                options={MARITAL_STATUS_OPTIONS}
                i18nEnum="maritalStatus"
                disabled={mutation.isPending}
              />
              <NumberInput
                id="card-personal-childCount"
                label={t("employees:fields.childCount")}
                error={childCountError}
                min={0}
                allowNegative={false}
                allowDecimal={false}
                inputMode="numeric"
                value={childCount}
                disabled={mutation.isPending}
                onChange={(v) => setChildCount(String(v))}
              />
              <NullableEnumSelect
                id="card-personal-educationStatus"
                label={t("employees:fields.educationStatus")}
                value={educationStatus}
                onChange={setEducationStatus}
                options={EDUCATION_STATUS_OPTIONS}
                i18nEnum="educationStatus"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
              <NullableEnumSelect
                id="card-personal-highestEducationLevel"
                label={t("employees:fields.highestEducationLevel")}
                value={highestEducationLevel}
                onChange={setHighestEducationLevel}
                options={HIGHEST_EDUCATION_LEVEL_OPTIONS}
                i18nEnum="highestEducationLevel"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending}
              />
              <NullableEnumSelect
                id="card-personal-militaryStatus"
                label={t("employees:fields.militaryStatus")}
                value={militaryStatus}
                onChange={setMilitaryStatus}
                options={MILITARY_STATUS_OPTIONS}
                i18nEnum="militaryStatus"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
              <NullableEnumSelect
                id="card-personal-disabilityDegree"
                label={t("employees:fields.disabilityDegree")}
                value={disabilityDegree}
                onChange={setDisabilityDegree}
                options={DISABILITY_DEGREE_OPTIONS}
                i18nEnum="disabilityDegree"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
              <NullableEnumSelect
                id="card-personal-bloodType"
                label={t("employees:fields.bloodType")}
                value={bloodType}
                onChange={setBloodType}
                options={BLOOD_TYPE_OPTIONS}
                i18nEnum="bloodType"
                emptyLabel={t("employees:enum.notSet")}
                disabled={mutation.isPending || !canViewSensitive}
              />
              <TextInput
                id="card-personal-mobilePhone"
                label={t("employees:fields.mobilePhone")}
                type="tel"
                value={mobilePhone}
                disabled={mutation.isPending}
                onChange={(e) => setMobilePhone(e.currentTarget.value)}
              />
              <TextInput
                id="card-personal-homePhone"
                label={t("employees:fields.homePhone")}
                type="tel"
                value={homePhone}
                disabled={mutation.isPending}
                onChange={(e) => setHomePhone(e.currentTarget.value)}
              />
              <TextInput
                id="card-personal-addressLine"
                label={t("employees:fields.addressLine")}
                value={addressLine}
                disabled={mutation.isPending}
                onChange={(e) => setAddressLine(e.currentTarget.value)}
              />
              <TextInput
                id="card-personal-city"
                label={t("employees:fields.city")}
                value={city}
                disabled={mutation.isPending}
                onChange={(e) => setCity(e.currentTarget.value)}
              />
              <TextInput
                id="card-personal-postalCode"
                label={t("employees:fields.postalCode")}
                value={postalCode}
                disabled={mutation.isPending}
                onChange={(e) => setPostalCode(e.currentTarget.value)}
              />
            </div>
            <EditFooter saving={mutation.isPending} onCancel={() => setEditing(false)} />
          </Stack>
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
      <Stack gap="lg">
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

        {!employee.userId && (
          <Text size="sm" c="dimmed">
            {t("employees:card.noAccountHint")}
          </Text>
        )}

        {showAccessTypeEditor && (
          <Paper withBorder p="md">
            <Stack gap="sm">
              <Radio.Group
                label={t("employees:fields.accessType")}
                description={t("employees:card.accessTypeHint")}
                error={accessError}
                value={accessType ?? ""}
                onChange={(value) => {
                  setAccessError(null);
                  setAccessType(value as AccessType);
                }}
                disabled={mutation.isPending}
              >
                <Group gap="md" mt="xs">
                  {ACCESS_TYPES.map((type) => (
                    <Radio key={type} id={`access-${type}`} value={type} label={t(`employees:accessType.${type}`)} />
                  ))}
                </Group>
              </Radio.Group>
              <Group justify="flex-end">
                <Button type="button" size="sm" disabled={!dirty || mutation.isPending} onClick={() => accessType && mutation.mutate(accessType)}>
                  {mutation.isPending ? t("common:saving") : t("common:save")}
                </Button>
              </Group>
            </Stack>
          </Paper>
        )}
      </Stack>
    </SectionCard>
  );
}
