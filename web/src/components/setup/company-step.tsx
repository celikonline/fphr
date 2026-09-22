import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Chip, Group, Select, Stack, Text, TextInput, Title } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  getCompanyProfile,
  updateCompanyProfile,
  type CompanyProfile,
  type WeekDay,
} from "@/services/company.service";
import {
  SUPPORTED_TENANT_LOCALES,
  ensureOrganization,
  getTenantProfile,
  updateTenantProfile,
  type TenantProfile,
} from "@/services/tenant-setup.service";
import { useAuthStore } from "@/store/auth.store";
import { StepActions, type SetupStepProps } from "./step-actions";

const WEEK_DAYS: WeekDay[] = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
const COMMON_TIME_ZONES = [
  "Europe/Istanbul",
  "Europe/London",
  "Europe/Berlin",
  "Europe/Paris",
  "America/New_York",
  "UTC",
];

const PREPARE_QUERY_KEY = ["setup", "prepare"] as const;

/**
 * Step 1 - company details. Loading it also makes sure the tenant's Organization defaults exist (the Worker normally
 * builds them from the TenantProvisioned event; this call doesn't wait for it), then reads both profiles.
 */
export default function CompanyStep(props: SetupStepProps) {
  const { t } = useTranslation(["setup", "common"]);
  const prepared = useQuery({
    queryKey: PREPARE_QUERY_KEY,
    queryFn: async () => {
      const tenant = await getTenantProfile();
      await ensureOrganization(tenant.name);
      const company = await getCompanyProfile();
      return { tenant, company };
    },
    staleTime: Infinity,
  });

  if (prepared.isError) {
    return (
      <Text c="red" role="alert">
        {t("setup:errors.load")}
      </Text>
    );
  }
  if (!prepared.data) {
    return <Text c="dimmed">{t("setup:preparing")}</Text>;
  }
  return <CompanyForm tenant={prepared.data.tenant} company={prepared.data.company} {...props} />;
}

function CompanyForm({
  tenant,
  company,
  onContinue,
}: SetupStepProps & { tenant: TenantProfile; company: CompanyProfile }) {
  const { t } = useTranslation(["setup", "common"]);
  const queryClient = useQueryClient();

  const [name, setName] = useState(tenant.name);
  const [taxNumber, setTaxNumber] = useState(tenant.taxNumber ?? company.taxNumber);
  const [locale, setLocale] = useState(tenant.locale);
  const [timeZone, setTimeZone] = useState(tenant.timeZone);
  const [address, setAddress] = useState(company.address);
  const [city, setCity] = useState(company.city);
  const [country, setCountry] = useState(company.country);
  const [workingDays, setWorkingDays] = useState<string[]>(company.workingDays);
  const [hoursStart, setHoursStart] = useState(company.operatingHours.start);
  const [hoursEnd, setHoursEnd] = useState(company.operatingHours.end);
  const [error, setError] = useState<string | null>(null);

  const timeZones = COMMON_TIME_ZONES.includes(tenant.timeZone)
    ? COMMON_TIME_ZONES
    : [tenant.timeZone, ...COMMON_TIME_ZONES];

  const mutation = useMutation({
    mutationFn: async () => {
      const trimmedName = name.trim();
      const trimmedTax = taxNumber.trim();
      await updateTenantProfile({
        name: trimmedName,
        taxNumber: trimmedTax || undefined,
        locale,
        timeZone,
      });
      await updateCompanyProfile({
        ...company,
        name: trimmedName,
        taxNumber: trimmedTax,
        address: address.trim(),
        city: city.trim(),
        country: country.trim(),
        operatingHours: { start: hoursStart, end: hoursEnd },
        workingDays: workingDays as WeekDay[],
      });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["company-profile"] });
      void useAuthStore.getState().refreshMe();
      onContinue();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("setup:errors.generic"))),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("setup:errors.nameRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <form onSubmit={handleSubmit}>
      <Stack gap="md">
        <div>
          <Title order={3} size="h4">
            {t("setup:company.heading")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("setup:company.description")}
          </Text>
        </div>

        <Group grow align="flex-start">
          <TextInput
            label={t("setup:company.name")}
            withAsterisk
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            label={t("setup:company.taxNumber")}
            value={taxNumber}
            maxLength={20}
            onChange={(event) => setTaxNumber(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
        </Group>

        <Group grow align="flex-start">
          <Select
            label={t("setup:company.locale")}
            allowDeselect={false}
            value={locale}
            data={SUPPORTED_TENANT_LOCALES.map((value) => ({
              value,
              label: t(`setup:company.locales.${value}`),
            }))}
            onChange={(value) => value && setLocale(value)}
            disabled={mutation.isPending}
          />
          <Select
            label={t("setup:company.timeZone")}
            allowDeselect={false}
            searchable
            value={timeZone}
            data={timeZones}
            onChange={(value) => value && setTimeZone(value)}
            disabled={mutation.isPending}
          />
        </Group>

        <TextInput
          label={t("setup:company.address")}
          value={address}
          onChange={(event) => setAddress(event.currentTarget.value)}
          disabled={mutation.isPending}
        />
        <Group grow align="flex-start">
          <TextInput
            label={t("setup:company.city")}
            value={city}
            onChange={(event) => setCity(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            label={t("setup:company.country")}
            value={country}
            onChange={(event) => setCountry(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
        </Group>

        <div>
          <Text size="sm" fw={500} mb={4}>
            {t("setup:company.workingDays")}
          </Text>
          <Chip.Group multiple value={workingDays} onChange={setWorkingDays}>
            <Group gap="xs">
              {WEEK_DAYS.map((day) => (
                <Chip key={day} value={day} size="sm" disabled={mutation.isPending}>
                  {t(`setup:company.days.${day}`)}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
        </div>
        <Group grow align="flex-start">
          <TextInput
            type="time"
            label={t("setup:company.hoursStart")}
            value={hoursStart}
            onChange={(event) => setHoursStart(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            type="time"
            label={t("setup:company.hoursEnd")}
            value={hoursEnd}
            onChange={(event) => setHoursEnd(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
        </Group>

        <StepActions isSubmitting={mutation.isPending} error={error} />
      </Stack>
    </form>
  );
}
