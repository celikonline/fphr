import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Select, Stack, Text, TextInput, Title } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  LOCATION_TYPES,
  createLocation,
  listLocations,
  type LocationType,
} from "@/services/locations.service";
import {
  assignCompanyHolidayCalendar,
  listCompanies,
  listHolidayCalendars,
  pickDefaultHolidayCalendar,
  type Company,
  type HolidayCalendar,
} from "@/services/structure.service";
import { StepActions, type SetupStepProps } from "./step-actions";

const DEFAULT_COUNTRY_CODE = "TR";

/** Step 2 - which holiday calendar the company follows, plus (optionally) its first location. */
export default function CalendarLocationStep(props: SetupStepProps) {
  const { t } = useTranslation(["setup", "common"]);
  const loaded = useQuery({
    queryKey: ["setup", "calendar-location"],
    queryFn: async () => {
      const [companies, calendars, locations] = await Promise.all([
        listCompanies(),
        listHolidayCalendars(),
        listLocations({ pageSize: 1 }),
      ]);
      return {
        company: companies[0] as Company | undefined,
        calendars,
        locationCount: locations.totalCount,
      };
    },
  });

  if (loaded.isError) {
    return (
      <Text c="red" role="alert">
        {t("setup:errors.load")}
      </Text>
    );
  }
  if (!loaded.data) {
    return <Text c="dimmed">{t("common:loading")}</Text>;
  }
  return <CalendarLocationForm {...loaded.data} {...props} />;
}

function CalendarLocationForm({
  company,
  calendars,
  locationCount,
  onContinue,
  onBack,
}: SetupStepProps & {
  company: Company | undefined;
  calendars: HolidayCalendar[];
  locationCount: number;
}) {
  const { t } = useTranslation(["setup", "common"]);
  const queryClient = useQueryClient();

  const initialCalendarId =
    company?.holidayCalendarId ?? pickDefaultHolidayCalendar(calendars)?.id ?? null;
  const [calendarId, setCalendarId] = useState<string | null>(initialCalendarId);
  const [locationName, setLocationName] = useState("");
  const [locationType, setLocationType] = useState<LocationType>("office");
  const [countryCode, setCountryCode] = useState(DEFAULT_COUNTRY_CODE);
  const [address, setAddress] = useState("");
  const [error, setError] = useState<string | null>(null);

  const canAddLocation = locationCount === 0;

  const mutation = useMutation({
    mutationFn: async () => {
      if (company && calendarId && calendarId !== company.holidayCalendarId) {
        await assignCompanyHolidayCalendar(company.id, calendarId);
      }
      if (canAddLocation && locationName.trim()) {
        await createLocation({
          name: locationName.trim(),
          type: locationType,
          countryCode: countryCode.trim().toUpperCase() || DEFAULT_COUNTRY_CODE,
          address: address.trim() || null,
        });
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["setup", "calendar-location"] });
      onContinue();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("setup:errors.generic"))),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    mutation.mutate();
  }

  return (
    <form onSubmit={handleSubmit}>
      <Stack gap="md">
        <div>
          <Title order={3} size="h4">
            {t("setup:calendar.heading")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("setup:calendar.description")}
          </Text>
        </div>

        <Select
          label={t("setup:calendar.calendarLabel")}
          placeholder={t("setup:calendar.calendarNone")}
          value={calendarId}
          data={calendars.map((calendar) => ({ value: calendar.id, label: calendar.name }))}
          onChange={setCalendarId}
          disabled={mutation.isPending}
        />

        <Title order={4} size="h5" mt="xs">
          {t("setup:calendar.locationHeading")}
        </Title>
        {canAddLocation ? (
          <Stack gap="sm">
            <TextInput
              label={t("setup:calendar.locationName")}
              description={t("setup:calendar.locationHint")}
              value={locationName}
              maxLength={200}
              onChange={(event) => setLocationName(event.currentTarget.value)}
              disabled={mutation.isPending}
              inputWrapperOrder={["label", "input", "description"]}
            />
            <Select
              label={t("setup:calendar.locationType")}
              allowDeselect={false}
              value={locationType}
              data={LOCATION_TYPES.map((value) => ({
                value,
                label: t(`setup:calendar.types.${value}`),
              }))}
              onChange={(value) => value && setLocationType(value as LocationType)}
              disabled={mutation.isPending}
            />
            <TextInput
              label={t("setup:calendar.locationCountry")}
              value={countryCode}
              maxLength={2}
              onChange={(event) => setCountryCode(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              label={t("setup:calendar.locationAddress")}
              value={address}
              onChange={(event) => setAddress(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
          </Stack>
        ) : (
          <Text size="sm" c="dimmed">
            {t("setup:calendar.locationExists", { count: locationCount })}
          </Text>
        )}

        <StepActions
          onBack={onBack}
          onSkip={onContinue}
          isSubmitting={mutation.isPending}
          error={error}
        />
      </Stack>
    </form>
  );
}
