import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Group, Input, Modal, Select, Stack, Textarea } from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import {
  OHS_INCIDENT_KINDS,
  closeIncident,
  markIncidentSgkNotified,
  recordIncident,
  reportNearMiss,
  type OhsIncident,
  type OhsIncidentKind,
} from "@/services/ohs-incidents.service";

/** Server limit (OhsLimits.IncidentDescriptionMaxLength). */
const TEXT_MAX_LENGTH = 2000;

const todayIso = () => toDateOnly(new Date());

export interface LocationOption {
  value: string;
  label: string;
}

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

interface IncidentFormDialogProps {
  /** HR records either kind with the injured employee; self-service reports a near miss only. */
  mode: "record" | "report";
  locations: LocationOption[];
  onClose: () => void;
  onSaved: () => void;
}

export function IncidentFormDialog({ mode, locations, onClose, onSaved }: IncidentFormDialogProps) {
  const { t } = useTranslation(["ohs", "common"]);
  const notify = useNotify();
  const [kind, setKind] = useState<OhsIncidentKind>(
    mode === "report" ? "nearMiss" : "workAccident"
  );
  const [eventDate, setEventDate] = useState(todayIso());
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [locationId, setLocationId] = useState<string | null>(null);
  const [description, setDescription] = useState("");

  const mutation = useMutation({
    mutationFn: () =>
      mode === "report"
        ? reportNearMiss({ eventDate, locationId, description: description.trim() })
        : recordIncident({
            kind,
            eventDate,
            locationId,
            employeeId: employee?.id ?? null,
            description: description.trim(),
          }),
    onSuccess: () => {
      notify.ok(mode === "report" ? t("ohs:incidents.reported") : t("ohs:incidents.recorded"));
      onSaved();
      onClose();
    },
    onError: notify.fail,
  });

  const needsEmployee = mode === "record" && kind === "workAccident";
  const canSubmit =
    eventDate !== "" &&
    description.trim().length > 0 &&
    (!needsEmployee || employee !== null) &&
    !mutation.isPending;
  const title = mode === "report" ? t("ohs:incidents.reportTitle") : t("ohs:incidents.recordTitle");

  return (
    <Modal opened onClose={onClose} title={title} centered size="lg">
      <Stack gap="md">
        {mode === "record" && (
          <Select
            id="incident-kind"
            label={t("ohs:incidents.fields.kind")}
            data={OHS_INCIDENT_KINDS.map((value) => ({
              value,
              label: t(`ohs:incidents.kinds.${value}`),
            }))}
            value={kind}
            allowDeselect={false}
            onChange={(value) => value && setKind(value as OhsIncidentKind)}
          />
        )}
        <Input.Wrapper
          label={t("ohs:incidents.fields.eventDate")}
          description={kind === "workAccident" ? t("ohs:incidents.sgkHint") : undefined}
          required
        >
          <DatePicker
            id="incident-date"
            value={eventDate}
            max={todayIso()}
            onChange={setEventDate}
          />
        </Input.Wrapper>
        {mode === "record" && (
          <EmployeePicker
            id="incident-employee"
            label={
              kind === "workAccident"
                ? t("ohs:incidents.fields.injuredEmployee")
                : t("ohs:incidents.fields.involvedEmployee")
            }
            value={employee}
            onChange={setEmployee}
            withAsterisk={needsEmployee}
            allowClear
          />
        )}
        {locations.length > 0 && (
          <Select
            id="incident-location"
            label={t("ohs:incidents.fields.location")}
            data={locations}
            value={locationId}
            onChange={setLocationId}
            clearable
            searchable
          />
        )}
        <Textarea
          id="incident-description"
          label={t("ohs:incidents.fields.description")}
          description={mode === "report" ? t("ohs:incidents.reportHint") : undefined}
          value={description}
          maxLength={TEXT_MAX_LENGTH}
          autosize
          minRows={4}
          required
          onChange={(e) => setDescription(e.currentTarget.value)}
        />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!canSubmit}
            loading={mutation.isPending}
          >
            {mode === "report" ? t("ohs:incidents.send") : t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

interface IncidentActionDialogProps {
  incident: OhsIncident;
  onClose: () => void;
  onDone: () => void;
}

/** Records the date the work accident was notified to SGK. */
export function SgkNotifiedDialog({ incident, onClose, onDone }: IncidentActionDialogProps) {
  const { t } = useTranslation(["ohs", "common"]);
  const notify = useNotify();
  const [notifiedOn, setNotifiedOn] = useState(todayIso());
  const mutation = useMutation({
    mutationFn: () => markIncidentSgkNotified(incident.id, notifiedOn),
    onSuccess: () => {
      notify.ok(t("ohs:incidents.sgkSaved"));
      onDone();
      onClose();
    },
    onError: notify.fail,
  });

  return (
    <Modal opened onClose={onClose} title={t("ohs:incidents.sgkTitle")} centered>
      <Stack gap="md">
        <Input.Wrapper label={t("ohs:incidents.fields.sgkNotifiedOn")} required>
          <DatePicker
            id="incident-sgk-date"
            value={notifiedOn}
            min={incident.eventDate}
            max={todayIso()}
            onChange={setNotifiedOn}
          />
        </Input.Wrapper>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!notifiedOn}
            loading={mutation.isPending}
          >
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Closes the incident with the corrective action taken. */
export function CloseIncidentDialog({ incident, onClose, onDone }: IncidentActionDialogProps) {
  const { t } = useTranslation(["ohs", "common"]);
  const notify = useNotify();
  const [action, setAction] = useState("");
  const mutation = useMutation({
    mutationFn: () => closeIncident(incident.id, action.trim()),
    onSuccess: () => {
      notify.ok(t("ohs:incidents.closedToast"));
      onDone();
      onClose();
    },
    onError: notify.fail,
  });

  return (
    <Modal opened onClose={onClose} title={t("ohs:incidents.closeTitle")} centered size="lg">
      <Stack gap="md">
        <Textarea
          id="incident-corrective-action"
          label={t("ohs:incidents.fields.correctiveAction")}
          description={t("ohs:incidents.correctiveHint")}
          value={action}
          maxLength={TEXT_MAX_LENGTH}
          autosize
          minRows={3}
          required
          onChange={(e) => setAction(e.currentTarget.value)}
        />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!action.trim()}
            loading={mutation.isPending}
          >
            {t("ohs:incidents.close")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
