import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Group, Input, Modal, NumberInput, Select, Stack, TextInput } from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import type { LocationOption } from "@/components/ohs/incident-dialogs";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import {
  CERTIFICATE_CLASSES,
  COMMITTEE_ROLES,
  SAFETY_SERVICE_ROLES,
  addCommitteeMember,
  createSafetyAssignment,
  type CertificateClass,
  type CommitteeRole,
  type SafetyServiceRole,
} from "@/services/ohs-services.service";

/** Server limit (OhsLimits.PersonNameMaxLength). */
const NAME_MAX_LENGTH = 200;

const todayIso = () => toDateOnly(new Date());

function useNotify() {
  const { t } = useTranslation(["ohs", "common"]);
  return {
    ok: (description: string) =>
      toast({ variant: "success", title: t("common:success"), description }),
    fail: (error: unknown) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("ohs:incidents.actionFailed")),
      }),
  };
}

function DialogActions({
  onClose,
  onSave,
  disabled,
  pending,
}: {
  onClose: () => void;
  onSave: () => void;
  disabled: boolean;
  pending: boolean;
}) {
  const { t } = useTranslation(["common"]);
  return (
    <Group justify="flex-end" gap="sm">
      <Button variant="default" onClick={onClose} disabled={pending}>
        {t("common:cancel")}
      </Button>
      <Button onClick={onSave} disabled={disabled} loading={pending}>
        {t("common:save")}
      </Button>
    </Group>
  );
}

interface CreateDialogProps {
  locations: LocationOption[];
  onClose: () => void;
  onSaved: () => void;
}

/** Uzman / hekim / DSP görevlendirmesi; kişi çoğu zaman OSGB çalışanıdır, bu yüzden ad serbest metindir. */
export function AssignmentDialog({ locations, onClose, onSaved }: CreateDialogProps) {
  const { t } = useTranslation(["ohs"]);
  const notify = useNotify();
  const [locationId, setLocationId] = useState<string | null>(locations[0]?.value ?? null);
  const [role, setRole] = useState<SafetyServiceRole>("safetyExpert");
  const [personName, setPersonName] = useState("");
  const [certificateClass, setCertificateClass] = useState<CertificateClass | null>(null);
  const [certificateNumber, setCertificateNumber] = useState("");
  const [providerName, setProviderName] = useState("");
  const [monthlyMinutes, setMonthlyMinutes] = useState<number | string>("");
  const [startDate, setStartDate] = useState(todayIso());
  const isExpert = role === "safetyExpert";

  const mutation = useMutation({
    mutationFn: () =>
      createSafetyAssignment({
        locationId: locationId!,
        role,
        personName: personName.trim(),
        employeeId: null,
        certificateClass: isExpert ? certificateClass : null,
        certificateNumber: certificateNumber.trim() || null,
        providerName: providerName.trim() || null,
        monthlyMinutes: Number(monthlyMinutes),
        startDate,
      }),
    onSuccess: () => {
      notify.ok(t("ohs:services.saved"));
      onSaved();
      onClose();
    },
    onError: notify.fail,
  });

  const canSubmit =
    !!locationId &&
    personName.trim().length > 0 &&
    Number(monthlyMinutes) > 0 &&
    (!isExpert || certificateClass !== null) &&
    startDate !== "" &&
    !mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={t("ohs:services.assignmentTitle")} centered size="lg">
      <Stack gap="md">
        <Select
          id="assignment-location"
          label={t("ohs:incidents.fields.location")}
          data={locations}
          value={locationId}
          onChange={setLocationId}
          searchable
          required
        />
        <Select
          id="assignment-role"
          label={t("ohs:services.fields.role")}
          data={SAFETY_SERVICE_ROLES.map((value) => ({
            value,
            label: t(`ohs:services.roles.${value}`),
          }))}
          value={role}
          allowDeselect={false}
          onChange={(value) => value && setRole(value as SafetyServiceRole)}
        />
        <TextInput
          id="assignment-person"
          label={t("ohs:services.fields.person")}
          value={personName}
          maxLength={NAME_MAX_LENGTH}
          required
          onChange={(e) => setPersonName(e.currentTarget.value)}
        />
        {isExpert && (
          <Group grow align="flex-start">
            <Select
              id="assignment-certificate"
              label={t("ohs:services.fields.certificateClass")}
              data={CERTIFICATE_CLASSES.map((value) => ({
                value,
                label: t(`ohs:services.certificates.${value}`),
              }))}
              value={certificateClass}
              onChange={(value) => setCertificateClass(value as CertificateClass | null)}
              required
            />
            <TextInput
              id="assignment-certificate-number"
              label={t("ohs:services.fields.certificateNumber")}
              value={certificateNumber}
              maxLength={NAME_MAX_LENGTH}
              onChange={(e) => setCertificateNumber(e.currentTarget.value)}
            />
          </Group>
        )}
        <TextInput
          id="assignment-provider"
          label={t("ohs:services.fields.provider")}
          description={t("ohs:services.providerHint")}
          value={providerName}
          maxLength={NAME_MAX_LENGTH}
          onChange={(e) => setProviderName(e.currentTarget.value)}
        />
        <Group grow align="flex-start">
          <NumberInput
            id="assignment-minutes"
            label={t("ohs:services.fields.monthlyMinutes")}
            description={t("ohs:services.minutesHint")}
            value={monthlyMinutes}
            min={1}
            allowDecimal={false}
            required
            onChange={setMonthlyMinutes}
          />
          <Input.Wrapper label={t("ohs:services.fields.startDate")} required>
            <DatePicker id="assignment-start" value={startDate} onChange={setStartDate} />
          </Input.Wrapper>
        </Group>
        <DialogActions
          onClose={onClose}
          onSave={() => mutation.mutate()}
          disabled={!canSubmit}
          pending={mutation.isPending}
        />
      </Stack>
    </Modal>
  );
}

/** İSG kurulu üyesi; bir çalışan seçilirse ad otomatik dolar. */
export function CommitteeMemberDialog({ locations, onClose, onSaved }: CreateDialogProps) {
  const { t } = useTranslation(["ohs"]);
  const notify = useNotify();
  const [locationId, setLocationId] = useState<string | null>(locations[0]?.value ?? null);
  const [role, setRole] = useState<CommitteeRole>("chair");
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [personName, setPersonName] = useState("");
  const [startDate, setStartDate] = useState(todayIso());

  const mutation = useMutation({
    mutationFn: () =>
      addCommitteeMember({
        locationId: locationId!,
        role,
        personName: personName.trim(),
        employeeId: employee?.id ?? null,
        startDate,
      }),
    onSuccess: () => {
      notify.ok(t("ohs:services.saved"));
      onSaved();
      onClose();
    },
    onError: notify.fail,
  });

  const pickEmployee = (picked: PickedEmployee | null) => {
    setEmployee(picked);
    if (picked) setPersonName(picked.displayName);
  };
  const canSubmit =
    !!locationId && personName.trim().length > 0 && startDate !== "" && !mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={t("ohs:services.memberTitle")} centered size="lg">
      <Stack gap="md">
        <Select
          id="member-location"
          label={t("ohs:incidents.fields.location")}
          data={locations}
          value={locationId}
          onChange={setLocationId}
          searchable
          required
        />
        <Select
          id="member-role"
          label={t("ohs:services.fields.committeeRole")}
          data={COMMITTEE_ROLES.map((value) => ({
            value,
            label: t(`ohs:services.committeeRoles.${value}`),
          }))}
          value={role}
          allowDeselect={false}
          onChange={(value) => value && setRole(value as CommitteeRole)}
        />
        <EmployeePicker
          id="member-employee"
          label={t("ohs:services.fields.employee")}
          value={employee}
          onChange={pickEmployee}
          allowClear
        />
        <TextInput
          id="member-person"
          label={t("ohs:services.fields.person")}
          value={personName}
          maxLength={NAME_MAX_LENGTH}
          required
          onChange={(e) => setPersonName(e.currentTarget.value)}
        />
        <Input.Wrapper label={t("ohs:services.fields.startDate")} required>
          <DatePicker id="member-start" value={startDate} onChange={setStartDate} />
        </Input.Wrapper>
        <DialogActions
          onClose={onClose}
          onSave={() => mutation.mutate()}
          disabled={!canSubmit}
          pending={mutation.isPending}
        />
      </Stack>
    </Modal>
  );
}

/** Ends an assignment or a committee membership on a date (not before it started). */
export function EndPeriodDialog({
  title,
  minDate,
  onEnd,
  onClose,
  onDone,
}: {
  title: string;
  minDate: string;
  onEnd: (endDate: string) => Promise<void>;
  onClose: () => void;
  onDone: () => void;
}) {
  const { t } = useTranslation(["ohs"]);
  const notify = useNotify();
  const [endDate, setEndDate] = useState(todayIso());
  const mutation = useMutation({
    mutationFn: () => onEnd(endDate),
    onSuccess: () => {
      notify.ok(t("ohs:services.ended"));
      onDone();
      onClose();
    },
    onError: notify.fail,
  });

  return (
    <Modal opened onClose={onClose} title={title} centered>
      <Stack gap="md">
        <Input.Wrapper label={t("ohs:services.fields.endDate")} required>
          <DatePicker id="period-end" value={endDate} min={minDate} onChange={setEndDate} />
        </Input.Wrapper>
        <DialogActions
          onClose={onClose}
          onSave={() => mutation.mutate()}
          disabled={!endDate}
          pending={mutation.isPending}
        />
      </Stack>
    </Modal>
  );
}
