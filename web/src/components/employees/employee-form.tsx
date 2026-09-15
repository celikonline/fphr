import { useTranslation } from "react-i18next";
import { TextInput } from "@mantine/core";
import { getApiProblem } from "@/lib/api-error";
import type { EmployeeDetail } from "@/services/employees.service";

/** "Genel" fields as form strings ("" = empty). */
export interface EmployeeGeneralValues {
  firstName: string;
  lastName: string;
  nationalId: string;
  workEmail: string;
  personalEmail: string;
  workPhone: string;
  hireDate: string;
  birthDate: string;
}

export type EmployeeGeneralField = keyof EmployeeGeneralValues;
export type FieldErrors = Partial<Record<EmployeeGeneralField, string>>;

export const EMPTY_GENERAL_VALUES: EmployeeGeneralValues = {
  firstName: "",
  lastName: "",
  nationalId: "",
  workEmail: "",
  personalEmail: "",
  workPhone: "",
  hireDate: "",
  birthDate: "",
};

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function generalValuesFrom(detail: EmployeeDetail): EmployeeGeneralValues {
  return {
    firstName: detail.firstName,
    lastName: detail.lastName,
    nationalId: detail.nationalId ?? "",
    workEmail: detail.workEmail ?? "",
    personalEmail: detail.personalEmail ?? "",
    workPhone: detail.workPhone ?? "",
    hireDate: detail.hireDate ?? "",
    birthDate: detail.birthDate ?? "",
  };
}

const orNull = (value: string) => (value.trim() === "" ? null : value.trim());

export function generalValuesToInput(values: EmployeeGeneralValues) {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    nationalId: orNull(values.nationalId),
    workEmail: orNull(values.workEmail),
    personalEmail: orNull(values.personalEmail),
    workPhone: orNull(values.workPhone),
    hireDate: orNull(values.hireDate),
    birthDate: orNull(values.birthDate),
  };
}

type TFn = (key: string) => string;

/** Light client checks; the server stays authoritative (TCKN checksum, uniqueness). */
export function validateGeneral(values: EmployeeGeneralValues, t: TFn): FieldErrors {
  const errors: FieldErrors = {};
  if (!values.firstName.trim()) errors.firstName = t("common:required");
  if (!values.lastName.trim()) errors.lastName = t("common:required");
  if (values.nationalId.trim() && !/^\d{11}$/.test(values.nationalId.trim())) errors.nationalId = t("employees:validation.nationalId");
  if (values.workEmail.trim() && !EMAIL_PATTERN.test(values.workEmail.trim())) errors.workEmail = t("employees:validation.email");
  if (values.personalEmail.trim() && !EMAIL_PATTERN.test(values.personalEmail.trim())) errors.personalEmail = t("employees:validation.email");
  return errors;
}

/** Maps ProblemDetails `errors` (PascalCase property names) onto form fields. */
export function serverFieldErrors(error: unknown): FieldErrors {
  const problem = getApiProblem(error);
  const result: FieldErrors = {};
  for (const [key, messages] of Object.entries(problem?.errors ?? {})) {
    const field = (key.charAt(0).toLowerCase() + key.slice(1)) as EmployeeGeneralField;
    if (field in EMPTY_GENERAL_VALUES && messages.length > 0) result[field] = messages[0];
  }
  return result;
}

interface EmployeeGeneralFieldsProps {
  idPrefix: string;
  values: EmployeeGeneralValues;
  errors: FieldErrors;
  onChange: (field: EmployeeGeneralField, value: string) => void;
  /** Only render these fields (default: all), e.g. the create dialog's short form. */
  fields?: EmployeeGeneralField[];
  disabled?: boolean;
  /** Individually-disabled fields, e.g. a masked nationalId without employee.sensitive.view. */
  disabledFields?: ReadonlySet<EmployeeGeneralField>;
}

const FIELD_TYPES: Record<EmployeeGeneralField, string> = {
  firstName: "text",
  lastName: "text",
  nationalId: "text",
  workEmail: "email",
  personalEmail: "email",
  workPhone: "tel",
  hireDate: "date",
  birthDate: "date",
};

const ALL_FIELDS = Object.keys(FIELD_TYPES) as EmployeeGeneralField[];
const REQUIRED: ReadonlySet<EmployeeGeneralField> = new Set(["firstName", "lastName"]);

export function EmployeeGeneralFields({
  idPrefix,
  values,
  errors,
  onChange,
  fields = ALL_FIELDS,
  disabled,
  disabledFields,
}: EmployeeGeneralFieldsProps) {
  const { t } = useTranslation(["employees"]);

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {fields.map((field) => (
        <TextInput
          key={field}
          id={`${idPrefix}-${field}`}
          label={t(`employees:fields.${field}`)}
          withAsterisk={REQUIRED.has(field)}
          error={errors[field]}
          type={FIELD_TYPES[field]}
          inputMode={field === "nationalId" ? "numeric" : undefined}
          maxLength={field === "nationalId" ? 11 : undefined}
          value={values[field]}
          disabled={disabled || disabledFields?.has(field)}
          onChange={(e) => onChange(field, e.currentTarget.value)}
        />
      ))}
    </div>
  );
}
