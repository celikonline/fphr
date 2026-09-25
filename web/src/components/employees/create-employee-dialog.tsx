import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button, Group, Stack, Switch, Text } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { useFormDraft } from "@/hooks/use-form-draft";
import { getApiErrorMessage } from "@/lib/api-error";
import { usePermission } from "@/components/permission-guard";
import { FormModal } from "@/components/ui/form-modal";
import { createEmployee } from "@/services/employees.service";
import { inviteEmployee } from "@/services/invitations.service";
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

/** localStorage-persisted subset of the dialog's state (S-53 draft recovery, see use-form-draft.ts). */
interface CreateEmployeeDraft {
  values: EmployeeGeneralValues;
  orgUnitId: string | null;
  manager: PickedEmployee | null;
}

const DRAFT_STORAGE_KEY = "senseik.draft.employee-create";

function isDraftEmpty(draft: CreateEmployeeDraft): boolean {
  return !draft.orgUnitId && !draft.manager && Object.values(draft.values).every((value) => value.trim() === "");
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
  const canInvite = usePermission("identity.users.invite");
  const [sendCredentials, setSendCredentials] = useState(true);
  const [saveAndNew, setSaveAndNew] = useState(false);

  const { clearDraft } = useFormDraft<CreateEmployeeDraft>({
    storageKey: DRAFT_STORAGE_KEY,
    value: { values, orgUnitId, manager },
    enabled: open,
    isEmpty: isDraftEmpty,
    onRestore: (draft) => {
      setValues(draft.values);
      setOrgUnitId(draft.orgUnitId);
      setManager(draft.manager);
    },
  });

  function reset() {
    setValues(EMPTY_GENERAL_VALUES);
    setOrgUnitId(null);
    setManager(null);
    setErrors({});
    setSendCredentials(true);
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const employee = await createEmployee({ ...generalValuesToInput(values), orgUnitId, managerEmployeeId: manager?.id ?? null });
      // The employee already exists here: an invitation failure is reported but never undoes the creation.
      // Without the invite permission or with the switch off the invite endpoint is not called at all.
      if (!canInvite || !sendCredentials) return { employee, invitation: null, inviteError: null, skipped: canInvite };
      try {
        return { employee, invitation: await inviteEmployee(employee.id, { sendCredentials: true }), inviteError: null, skipped: false };
      } catch (error) {
        return { employee, invitation: null, inviteError: error, skipped: false };
      }
    },
    onSuccess: ({ employee, inviteError, skipped }) => {
      queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:create.success") });
      if (inviteError) {
        toast({
          variant: "destructive",
          title: t("common:error"),
          description: t("employees:create.inviteFailed", { message: getApiErrorMessage(inviteError, t("employees:genericError")) }),
        });
      } else if (skipped) {
        toast({ variant: "default", title: t("employees:invite.outcome.notrequested") });
      }
      clearDraft();
      reset();
      if (saveAndNew) return;
      onOpenChange(false);
      navigate(`/app/employees/${employee.id}`);
    },
    onError: (error) => {
      setErrors(serverFieldErrors(error));
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  function submit(andNew: boolean) {
    if (mutation.isPending) return;
    setSaveAndNew(andNew);
    const found = validateGeneral(values, t);
    setErrors(found);
    if (Object.keys(found).length === 0) mutation.mutate();
  }

  return (
    <FormModal
      opened={open}
      onClose={() => {
        reset();
        onOpenChange(false);
      }}
      title={t("employees:create.title")}
      size="42rem"
    >
      <Text size="sm" c="dimmed" mb="md">
        {t("employees:create.description")}
      </Text>
      <form
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          submit(false);
        }}
      >
        <Stack gap="md">
          <EmployeeGeneralFields
            idPrefix="new-emp"
            values={values}
            errors={errors}
            disabled={mutation.isPending}
            onChange={(field, value) => {
              setValues((prev) => ({ ...prev, [field]: value }));
              setErrors((prev) => ({ ...prev, [field]: undefined }));
            }}
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <OrgUnitSelect
              id="new-emp-unit"
              label={t("employees:fields.orgUnit")}
              value={orgUnitId}
              onChange={setOrgUnitId}
              emptyLabel={t("employees:noUnit")}
            />
            <EmployeePicker id="new-emp-manager" label={t("employees:fields.manager")} value={manager} onChange={setManager} allowClear />
          </div>
          {canInvite && (
          <Switch
            checked={sendCredentials}
            onChange={(event) => setSendCredentials(event.currentTarget.checked)}
            disabled={mutation.isPending}
            label={t("employees:create.sendCredentials")}
          />
          )}
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              onClick={() => {
                reset();
                onOpenChange(false);
              }}
              disabled={mutation.isPending}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              variant="light"
              onClick={() => submit(true)}
              loading={mutation.isPending && saveAndNew}
              disabled={mutation.isPending}
            >
              {t("common:saveAndNew")}
            </Button>
            <Button type="submit" loading={mutation.isPending && !saveAndNew} disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}
