import { useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import { Button, Card, Group, Modal, MultiSelect, NumberInput, Select, Stack, Text, TextInput, Title } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  setEmployeeCustomFields,
  updateMyProfile,
  type CustomFieldValue,
  type EmployeeDetail,
} from "@/services/employees.service";
import { CUSTOM_FIELD_SECTIONS, type CustomFieldSection } from "@/services/custom-fields.service";
import { useAuthStore } from "@/store/auth.store";
import type { EmployeeCardTabProps } from "./card-tabs";

const NOT_SET = "__not_set__";

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

function parseMultiSelect(value: string | null): string[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === "string") : [];
  } catch {
    return [];
  }
}

/** Renders the right input for a custom field's data type; `value`/`onChange` carry the raw string the API expects. */
function CustomFieldValueInput({
  field,
  value,
  onChange,
  disabled,
}: {
  field: CustomFieldValue;
  value: string | null;
  onChange: (value: string | null) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation(["employees", "common"]);
  const id = `cf-value-${field.definitionId}`;

  switch (field.dataType) {
    case "number":
      return (
        <NumberInput
          id={id}
          label={field.name}
          required={field.isRequired}
          disabled={disabled}
          value={value ?? ""}
          onChange={(v) => onChange(v === "" || v === undefined ? null : String(v))}
        />
      );
    case "date":
      return (
        <Stack gap={4}>
          <Text size="sm" fw={500}>
            {field.name}
          </Text>
          <DatePicker id={id} value={value ?? ""} disabled={disabled} clearable onChange={(v) => onChange(v || null)} />
        </Stack>
      );
    case "boolean":
      return (
        <Select
          id={id}
          label={field.name}
          disabled={disabled}
          allowDeselect={false}
          data={[
            { value: NOT_SET, label: t("employees:customFields.notSet") },
            { value: "true", label: t("common:yes") },
            { value: "false", label: t("common:no") },
          ]}
          value={value === "true" || value === "false" ? value : NOT_SET}
          onChange={(v) => onChange(v === NOT_SET || v === null ? null : v)}
        />
      );
    case "singleSelect":
      return (
        <Select
          id={id}
          label={field.name}
          required={field.isRequired}
          disabled={disabled}
          clearable
          data={field.options}
          value={value}
          onChange={onChange}
        />
      );
    case "multiSelect":
      return (
        <MultiSelect
          id={id}
          label={field.name}
          required={field.isRequired}
          disabled={disabled}
          data={field.options}
          value={parseMultiSelect(value)}
          onChange={(selected) => onChange(selected.length > 0 ? JSON.stringify(selected) : null)}
        />
      );
    case "text":
    default:
      return (
        <TextInput
          id={id}
          label={field.name}
          required={field.isRequired}
          disabled={disabled}
          value={value ?? ""}
          onChange={(e) => onChange(e.currentTarget.value || null)}
        />
      );
  }
}

function displayValue(field: CustomFieldValue, t: (key: string) => string): string {
  if (field.value === null) return "—";
  if (field.dataType === "boolean") return field.value === "true" ? t("common:yes") : t("common:no");
  if (field.dataType === "multiSelect") {
    const items = parseMultiSelect(field.value);
    return items.length > 0 ? items.join(", ") : "—";
  }

  return field.value;
}

/** "Özel alanlar" (E4): custom field values grouped by section, editable by HR (any field) or the card owner (their own IsEmployeeEditable fields). */
export function CustomFieldsSection({ employee }: EmployeeCardTabProps) {
  const { t } = useTranslation(["employees", "common"]);
  const canManage = usePermission("employee.employees.update");
  const canSelfEdit = usePermission("employee.self.edit");
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  const isOwnCard = Boolean(ownEmployeeId) && employee.id === ownEmployeeId;

  const fields = useMemo(() => employee.customFields ?? [], [employee.customFields]);
  const [hrEditOpen, setHrEditOpen] = useState(false);
  const [selfEditOpen, setSelfEditOpen] = useState(false);

  const grouped = useMemo(() => {
    const bySection = new Map<CustomFieldSection, CustomFieldValue[]>();
    for (const field of fields) {
      const list = bySection.get(field.section) ?? [];
      list.push(field);
      bySection.set(field.section, list);
    }

    return bySection;
  }, [fields]);

  const editableFields = fields.filter((f) => f.isEmployeeEditable);

  return (
    <SectionCard
      title={t("employees:card.sections.customFields")}
      action={
        <Group gap="xs">
          {isOwnCard && canSelfEdit && !canManage && (
            <Button type="button" variant="subtle" size="sm" onClick={() => setSelfEditOpen(true)}>
              {t("employees:customFields.editMine")}
            </Button>
          )}
          {canManage && fields.length > 0 && (
            <Button type="button" variant="subtle" size="sm" leftSection={<Pencil size={16} />} onClick={() => setHrEditOpen(true)}>
              {t("common:edit")}
            </Button>
          )}
        </Group>
      }
    >
      {fields.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:customFields.empty")}
        </Text>
      ) : (
        <Stack gap="lg">
          {CUSTOM_FIELD_SECTIONS.filter((section) => grouped.has(section)).map((section) => (
            <div key={section}>
              <Text size="sm" fw={600} mb="xs">
                {t(`employees:customFields.section.${section}`)}
              </Text>
              <dl className="grid gap-4 sm:grid-cols-2">
                {grouped.get(section)!.map((field) => (
                  <Field key={field.definitionId} label={field.name}>
                    {displayValue(field, t)}
                  </Field>
                ))}
              </dl>
            </div>
          ))}
        </Stack>
      )}

      {hrEditOpen && <HrEditDialog employee={employee} fields={fields} onClose={() => setHrEditOpen(false)} />}
      {selfEditOpen && <SelfEditDialog employee={employee} fields={editableFields} onClose={() => setSelfEditOpen(false)} />}
    </SectionCard>
  );
}

/** HR/admin edit: every active definition, via PUT /employees/{id}/custom-fields. */
function HrEditDialog({ employee, fields, onClose }: { employee: EmployeeDetail; fields: CustomFieldValue[]; onClose: () => void }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const [values, setValues] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(fields.map((f) => [f.definitionId, f.value])),
  );

  const mutation = useMutation({
    mutationFn: () => setEmployeeCustomFields(employee.id, values),
    onSuccess: (updated) => {
      queryClient.setQueryData<EmployeeDetail>(["employees", "detail", employee.id], (prev) =>
        prev ? { ...prev, customFields: updated } : prev,
      );
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:card.saved") });
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) }),
  });

  return (
    <Modal opened onClose={() => !mutation.isPending && onClose()} title={t("employees:customFields.editTitle")} size="lg">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <Stack gap="md">
          {fields.map((field) => (
            <CustomFieldValueInput
              key={field.definitionId}
              field={field}
              value={values[field.definitionId] ?? null}
              disabled={mutation.isPending}
              onChange={(value) => setValues((prev) => ({ ...prev, [field.definitionId]: value }))}
            />
          ))}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

/** Self-service edit ("Self-servis düzenleme"): own contact fields + own IsEmployeeEditable custom fields, via PUT /employees/me/profile. */
function SelfEditDialog({ employee, fields, onClose }: { employee: EmployeeDetail; fields: CustomFieldValue[]; onClose: () => void }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const [personalEmail, setPersonalEmail] = useState(employee.personalEmail ?? "");
  const [mobilePhone, setMobilePhone] = useState(employee.mobilePhone ?? "");
  const [homePhone, setHomePhone] = useState(employee.homePhone ?? "");
  const [addressLine, setAddressLine] = useState(employee.addressLine ?? "");
  const [city, setCity] = useState(employee.city ?? "");
  const [postalCode, setPostalCode] = useState(employee.postalCode ?? "");
  const [values, setValues] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(fields.map((f) => [f.definitionId, f.value])),
  );

  const mutation = useMutation({
    mutationFn: () =>
      updateMyProfile({
        personalEmail: personalEmail.trim() || null,
        mobilePhone: mobilePhone.trim() || null,
        homePhone: homePhone.trim() || null,
        addressLine: addressLine.trim() || null,
        city: city.trim() || null,
        postalCode: postalCode.trim() || null,
        customFieldValues: fields.length > 0 ? values : null,
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(["employees", "detail", employee.id], updated);
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:card.saved") });
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) }),
  });

  return (
    <Modal opened onClose={() => !mutation.isPending && onClose()} title={t("employees:customFields.editMineTitle")} size="lg">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <Stack gap="md">
          <Text size="sm" fw={600}>
            {t("employees:customFields.contactFields")}
          </Text>
          <TextInput
            id="self-personalEmail"
            label={t("employees:fields.personalEmail")}
            type="email"
            value={personalEmail}
            disabled={mutation.isPending}
            onChange={(e) => setPersonalEmail(e.currentTarget.value)}
          />
          <TextInput
            id="self-mobilePhone"
            label={t("employees:fields.mobilePhone")}
            type="tel"
            value={mobilePhone}
            disabled={mutation.isPending}
            onChange={(e) => setMobilePhone(e.currentTarget.value)}
          />
          <TextInput
            id="self-homePhone"
            label={t("employees:fields.homePhone")}
            type="tel"
            value={homePhone}
            disabled={mutation.isPending}
            onChange={(e) => setHomePhone(e.currentTarget.value)}
          />
          <TextInput
            id="self-addressLine"
            label={t("employees:fields.addressLine")}
            value={addressLine}
            disabled={mutation.isPending}
            onChange={(e) => setAddressLine(e.currentTarget.value)}
          />
          <TextInput
            id="self-city"
            label={t("employees:fields.city")}
            value={city}
            disabled={mutation.isPending}
            onChange={(e) => setCity(e.currentTarget.value)}
          />
          <TextInput
            id="self-postalCode"
            label={t("employees:fields.postalCode")}
            value={postalCode}
            disabled={mutation.isPending}
            onChange={(e) => setPostalCode(e.currentTarget.value)}
          />
          {fields.length > 0 && (
            <>
              <Text size="sm" fw={600} mt="sm">
                {t("employees:card.sections.customFields")}
              </Text>
              {fields.map((field) => (
                <CustomFieldValueInput
                  key={field.definitionId}
                  field={field}
                  value={values[field.definitionId] ?? null}
                  disabled={mutation.isPending}
                  onChange={(value) => setValues((prev) => ({ ...prev, [field.definitionId]: value }))}
                />
              ))}
            </>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
