import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Collapse, Drawer, Group, Radio, Select, Stack, Switch, Text, Textarea, TextInput } from "@mantine/core";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import EmployeeMultiPicker from "@/components/employees/employee-multi-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { TimePicker } from "@/components/ui/time-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  createCompanyEvent,
  listCompanyEventCategories,
  updateCompanyEvent,
  type CompanyCalendarEntry,
  type CompanyEventMode,
} from "@/services/company-calendar.service";
import type { EmployeeSelection } from "@/types";
import EventCategoryManager, { EVENT_CATEGORIES_QUERY_KEY } from "./event-category-manager";
import {
  buildEventPayload,
  emptyEventForm,
  eventFormFromEntry,
  resolveSelectionToIds,
  validateEventForm,
  type EventFormError,
  type EventFormValues,
  type ManualEventType,
} from "./event-form-model";

const MANUAL_TYPES: ManualEventType[] = ["companyEvent", "meeting"];
const CALENDAR_QUERY_KEY = "companyCalendar";
const NO_MODE = "none";

interface EventFormPanelProps {
  /** Entry to edit; omitted → create. */
  entry?: CompanyCalendarEntry | null;
  onClose: () => void;
}

/** Right-hand drawer to create or edit a company event (E4). Edit sends the full payload (PUT replaces). */
export default function EventFormPanel({ entry, onClose }: EventFormPanelProps) {
  const { t } = useTranslation(["companyCalendar", "common", "employees"]);
  const queryClient = useQueryClient();
  const [values, setValues] = useState<EventFormValues>(() => (entry ? eventFormFromEntry(entry) : emptyEventForm()));
  const [errors, setErrors] = useState<EventFormError[]>([]);
  const [serverError, setServerError] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [categoriesOpen, setCategoriesOpen] = useState(false);
  const [resolving, setResolving] = useState(false);

  const categories = useQuery({ queryKey: [EVENT_CATEGORIES_QUERY_KEY], queryFn: listCompanyEventCategories });
  const categoryList = categories.data ?? [];
  const selectedCategory = categoryList.find((c) => c.id === values.categoryId);

  const set = <K extends keyof EventFormValues>(key: K, value: EventFormValues[K]) => setValues((v) => ({ ...v, [key]: value }));

  const mutation = useMutation({
    mutationFn: async () => {
      const payload = buildEventPayload(values);
      if (entry?.id) await updateCompanyEvent(entry.id, payload);
      else await createCompanyEvent(payload);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CALENDAR_QUERY_KEY] });
      toast({ variant: "success", title: t("common:success"), description: t(entry ? "companyCalendar:updated" : "companyCalendar:saved") });
      onClose();
    },
    onError: (err) => {
      const code = getApiProblem(err)?.code;
      setServerError(
        code === "company_event.title_required"
          ? t("companyCalendar:errors.titleRequired")
          : getApiErrorMessage(err, t("companyCalendar:errors.generic"))
      );
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateEventForm(values);
    setErrors(found);
    setServerError(null);
    if (found.length === 0) mutation.mutate();
  }

  async function onInviteesPicked(selection: EmployeeSelection) {
    setResolving(true);
    try {
      const ids = await resolveSelectionToIds(selection);
      set("inviteeEmployeeIds", ids);
      setPickerOpen(false);
    } catch (err) {
      setServerError(getApiErrorMessage(err, t("companyCalendar:errors.generic")));
    } finally {
      setResolving(false);
    }
  }

  const err = (key: EventFormError) => (errors.includes(key) ? t(`companyCalendar:errors.${key}`) : undefined);
  const busy = mutation.isPending;

  return (
    <Drawer
      opened
      onClose={onClose}
      position="right"
      size="34rem"
      title={t(entry ? "companyCalendar:edit" : "companyCalendar:create")}
      closeButtonProps={{ "aria-label": t("common:close") }}
    >
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <TextInput
            label={t("companyCalendar:form.title")}
            placeholder={t("companyCalendar:form.titlePlaceholder")}
            withAsterisk
            value={values.title}
            maxLength={200}
            error={err("titleRequired")}
            onChange={(e) => set("title", e.currentTarget.value)}
            disabled={busy}
          />
          <Select
            label={t("companyCalendar:form.category")}
            placeholder={t("companyCalendar:form.categoryNone")}
            data={categoryList.map((c) => ({ value: c.id, label: c.name }))}
            value={values.categoryId}
            clearable
            onChange={(value) => set("categoryId", value)}
            disabled={busy}
            leftSection={
              selectedCategory ? (
                <span aria-hidden className="inline-block h-3 w-3 rounded-full" style={{ backgroundColor: selectedCategory.color }} />
              ) : undefined
            }
          />
          <Button type="button" variant="subtle" size="xs" w="fit-content" onClick={() => setCategoriesOpen((o) => !o)} aria-expanded={categoriesOpen}>
            {t("companyCalendar:categories.manage")}
          </Button>
          <Collapse expanded={categoriesOpen}>
            <EventCategoryManager categories={categoryList} />
          </Collapse>

          <Radio.Group label={t("companyCalendar:form.type")} value={values.type} onChange={(v) => set("type", v as ManualEventType)}>
            <Group gap="md" mt={4}>
              {MANUAL_TYPES.map((value) => (
                <Radio key={value} value={value} label={t(`companyCalendar:type.${value}`)} disabled={busy} />
              ))}
            </Group>
          </Radio.Group>

          <Switch
            label={t("companyCalendar:form.allDay")}
            checked={values.isAllDay}
            onChange={(e) => set("isAllDay", e.currentTarget.checked)}
            disabled={busy}
          />
          <Group grow align="flex-start">
            <Stack gap={4}>
              <Text size="sm" fw={500} id="event-start-label">
                {t("companyCalendar:form.startsAt")}
              </Text>
              <DatePicker id="event-start-date" value={values.startDate} onChange={(v) => set("startDate", v)} disabled={busy} />
              {!values.isAllDay && <TimePicker id="event-start-time" value={values.startTime} onChange={(v) => set("startTime", v)} disabled={busy} />}
            </Stack>
            <Stack gap={4}>
              <Text size="sm" fw={500} id="event-end-label">
                {t("companyCalendar:form.endsAt")}
              </Text>
              <DatePicker id="event-end-date" value={values.endDate} onChange={(v) => set("endDate", v)} disabled={busy} />
              {!values.isAllDay && <TimePicker id="event-end-time" value={values.endTime} onChange={(v) => set("endTime", v)} disabled={busy} />}
            </Stack>
          </Group>
          {err("endsBeforeStarts") && (
            <Text size="sm" c="red" role="alert">
              {err("endsBeforeStarts")}
            </Text>
          )}

          <Radio.Group
            label={t("companyCalendar:form.mode")}
            value={values.mode ?? NO_MODE}
            onChange={(v) => set("mode", v === NO_MODE ? null : (v as CompanyEventMode))}
          >
            <Group gap="md" mt={4}>
              <Radio value={NO_MODE} label={t("companyCalendar:mode.none")} disabled={busy} />
              <Radio value="onsite" label={t("companyCalendar:mode.onsite")} disabled={busy} />
              <Radio value="online" label={t("companyCalendar:mode.online")} disabled={busy} />
            </Group>
          </Radio.Group>
          {values.mode === "onsite" && (
            <TextInput
              label={t("companyCalendar:form.location")}
              withAsterisk
              value={values.location}
              maxLength={300}
              error={err("locationRequired")}
              onChange={(e) => set("location", e.currentTarget.value)}
              disabled={busy}
            />
          )}
          {values.mode === "online" && (
            <TextInput
              label={t("companyCalendar:form.onlineUrl")}
              placeholder="https://"
              withAsterisk
              value={values.onlineUrl}
              error={err("onlineUrlRequired")}
              onChange={(e) => set("onlineUrl", e.currentTarget.value)}
              disabled={busy}
            />
          )}

          <Textarea
            label={t("companyCalendar:form.description")}
            placeholder={t("companyCalendar:form.descriptionPlaceholder")}
            value={values.description}
            maxLength={2000}
            autosize
            minRows={2}
            onChange={(e) => set("description", e.currentTarget.value)}
            disabled={busy}
          />

          <Radio.Group
            label={t("companyCalendar:form.visibility")}
            value={values.visibilityOrgUnitId === null ? "all" : "orgUnit"}
            onChange={(v) => set("visibilityOrgUnitId", v === "all" ? null : (values.visibilityOrgUnitId ?? ""))}
          >
            <Group gap="md" mt={4}>
              <Radio value="all" label={t("companyCalendar:form.visibilityAll")} disabled={busy} />
              <Radio value="orgUnit" label={t("companyCalendar:form.visibilityOrgUnit")} disabled={busy} />
            </Group>
          </Radio.Group>
          {values.visibilityOrgUnitId !== null && (
            <OrgUnitSelect
              id="company-event-org-unit"
              value={values.visibilityOrgUnitId || null}
              onChange={(v) => set("visibilityOrgUnitId", v ?? "")}
              emptyLabel={t("employees:filters.allUnits")}
              disabled={busy}
            />
          )}

          <Stack gap={6}>
            <Text size="sm" fw={500}>
              {t("companyCalendar:form.invitees")}
            </Text>
            <Group gap="xs">
              <Badge variant="light" data-testid="invitee-count">
                {t("companyCalendar:form.inviteeCount", { count: values.inviteeEmployeeIds.length })}
              </Badge>
              <Button type="button" variant="light" size="xs" onClick={() => setPickerOpen((o) => !o)} loading={resolving}>
                {t("companyCalendar:form.selectInvitees")}
              </Button>
              {values.inviteeEmployeeIds.length > 0 && (
                <Button type="button" variant="subtle" size="xs" onClick={() => set("inviteeEmployeeIds", [])}>
                  {t("companyCalendar:form.clearInvitees")}
                </Button>
              )}
            </Group>
            <Collapse expanded={pickerOpen}>{pickerOpen && <EmployeeMultiPicker onConfirm={(s) => void onInviteesPicked(s)} />}</Collapse>
            <Switch
              label={t("companyCalendar:form.notifyInvitees")}
              checked={values.notifyInvitees}
              onChange={(e) => set("notifyInvitees", e.currentTarget.checked)}
              disabled={busy || values.inviteeEmployeeIds.length === 0}
            />
          </Stack>

          {serverError && (
            <Text size="sm" c="red" role="alert">
              {serverError}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={busy}>
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Drawer>
  );
}
