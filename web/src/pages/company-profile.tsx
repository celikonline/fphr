import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, Info } from "lucide-react";
import { Alert, Avatar, Badge, Button, Card, Chip, Group, Input, Skeleton, Text, TextInput, Title } from "@mantine/core";
import { FileDropzone, type FileRejectReason } from "@/components/ui/file-dropzone";
import { TimePicker } from "@/components/ui/time-picker";
import { toast } from "@/hooks/use-toast";
import { usePermission } from "@/components/permission-guard";
import {
  getCompanyProfile,
  updateCompanyProfile,
  uploadCompanyLogo,
  type CompanyProfile,
  type WeekDay,
} from "@/services/company.service";
import { getActiveIncentiveSummary } from "@/services/payroll.service";

const LOGO_MAX_BYTES = 2 * 1024 * 1024;
const WEEK_DAYS: WeekDay[] = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

const schema = z.object({
  name: z.string().min(1),
  registrationNumber: z.string().min(1),
  taxNumber: z.string().min(1),
  address: z.string().min(1),
  city: z.string().min(1),
  country: z.string().min(1),
  operatingHoursStart: z.string().min(1),
  operatingHoursEnd: z.string().min(1),
  workingDays: z.array(z.enum(["mon", "tue", "wed", "thu", "fri", "sat", "sun"])),
});

type FormValues = z.infer<typeof schema>;

function toFormValues(profile: CompanyProfile): FormValues {
  return {
    name: profile.name,
    registrationNumber: profile.registrationNumber,
    taxNumber: profile.taxNumber,
    address: profile.address,
    city: profile.city,
    country: profile.country,
    operatingHoursStart: profile.operatingHours.start,
    operatingHoursEnd: profile.operatingHours.end,
    workingDays: profile.workingDays,
  };
}

export default function CompanyProfilePage() {
  const { t } = useTranslation(["company", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.tenant.manage");
  const canViewIncentives = usePermission("payroll.profile.manage");

  const { data: profile, isLoading } = useQuery({
    queryKey: ["company-profile"],
    queryFn: getCompanyProfile,
  });

  // senseik-010/F-03: read-only "aktif teşvik türleri" summary (handoff §3 - the writable value stays on the
  // employee's Bordro parametreleri, this screen only shows which teşvik codes are currently in effect).
  const { data: incentiveSummary } = useQuery({
    queryKey: ["company-profile", "incentive-summary"],
    queryFn: getActiveIncentiveSummary,
    enabled: canViewIncentives,
  });

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: profile ? toFormValues(profile) : undefined,
  });

  useEffect(() => {
    if (profile) reset(toFormValues(profile));
  }, [profile, reset]);

  const saveMutation = useMutation({
    mutationFn: (values: FormValues) =>
      updateCompanyProfile({
        name: values.name,
        registrationNumber: values.registrationNumber,
        taxNumber: values.taxNumber,
        address: values.address,
        city: values.city,
        country: values.country,
        operatingHours: { start: values.operatingHoursStart, end: values.operatingHoursEnd },
        workingDays: values.workingDays,
      }),
    onMutate: async (values) => {
      await queryClient.cancelQueries({ queryKey: ["company-profile"] });
      const previous = queryClient.getQueryData<CompanyProfile>(["company-profile"]);
      if (previous) {
        queryClient.setQueryData<CompanyProfile>(["company-profile"], {
          ...previous,
          name: values.name,
          registrationNumber: values.registrationNumber,
          taxNumber: values.taxNumber,
          address: values.address,
          city: values.city,
          country: values.country,
          operatingHours: { start: values.operatingHoursStart, end: values.operatingHoursEnd },
          workingDays: values.workingDays,
        });
      }
      return { previous };
    },
    onError: (_err, _values, context) => {
      if (context?.previous) queryClient.setQueryData(["company-profile"], context.previous);
      toast({ variant: "destructive", title: t("common:error"), description: t("company:saveError") });
    },
    onSuccess: (updated) => {
      queryClient.setQueryData(["company-profile"], updated);
      toast({ variant: "success", title: t("common:success"), description: t("company:saveSuccess") });
    },
  });

  const logoMutation = useMutation({
    mutationFn: uploadCompanyLogo,
    onSuccess: ({ logoUrl }) => {
      queryClient.setQueryData<CompanyProfile | undefined>(["company-profile"], (prev) =>
        prev ? { ...prev, logoUrl } : prev
      );
      toast({ variant: "success", title: t("common:success"), description: t("company:logoSuccess") });
    },
    onError: () => {
      toast({ variant: "destructive", title: t("common:error"), description: t("company:logoError") });
    },
  });

  function handleLogoReject(reason: FileRejectReason) {
    const description = reason === "size" ? t("company:logoRejectedSize") : t("company:logoRejectedType");
    toast({ variant: "destructive", title: t("common:error"), description });
  }

  if (isLoading || !profile) {
    return (
      <div className="space-y-4 p-6 md:p-10">
        <Skeleton h={32} w={256} />
        <Skeleton h={256} />
      </div>
    );
  }

  return (
    <div className="max-w-3xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Building2 size={24} /> {t("company:title")}
        </Title>
        <Text c="dimmed">{t("company:subtitle")}</Text>
      </div>

      {!canManage && (
        <Alert variant="light" icon={<Info size={16} />}>
          {t("company:readOnlyNotice")}
        </Alert>
      )}

      <Card withBorder padding="lg">
        <Text fw={600}>{t("company:sectionLogo")}</Text>
        <Text size="sm" c="dimmed">
          {t("company:logoHint")}
        </Text>
        <div className="mt-4 flex items-center gap-4">
          <Avatar size={64} src={profile.logoUrl} alt={profile.name}>
            {profile.name.slice(0, 2).toUpperCase()}
          </Avatar>
          <FileDropzone
            className="flex-1"
            accept="image/png,image/jpeg"
            maxBytes={LOGO_MAX_BYTES}
            disabled={!canManage}
            busy={logoMutation.isPending}
            title={t("company:uploadLogo")}
            hint={t("company:logoDropHint")}
            busyLabel={t("company:uploadingLogo")}
            onFile={(file) => logoMutation.mutate(file)}
            onReject={handleLogoReject}
          />
        </div>
      </Card>

      <form
        onSubmit={handleSubmit((values) => saveMutation.mutate(values))}
        className="space-y-6"
        aria-label={t("company:title")}
      >
        <Card withBorder padding="lg">
          <Text fw={600} mb="md">
            {t("company:sectionGeneral")}
          </Text>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <TextInput
              id="name"
              className="sm:col-span-2"
              label={t("company:name")}
              disabled={!canManage}
              error={errors.name ? t("common:required") : undefined}
              {...register("name")}
            />
            <TextInput
              id="registrationNumber"
              label={t("company:registrationNumber")}
              disabled={!canManage}
              {...register("registrationNumber")}
            />
            <TextInput id="taxNumber" label={t("company:taxNumber")} disabled={!canManage} {...register("taxNumber")} />
            <TextInput
              id="address"
              className="sm:col-span-2"
              label={t("company:address")}
              disabled={!canManage}
              {...register("address")}
            />
            <TextInput id="city" label={t("company:city")} disabled={!canManage} {...register("city")} />
            <TextInput id="country" label={t("company:country")} disabled={!canManage} {...register("country")} />
          </div>
        </Card>

        <Card withBorder padding="lg">
          <Text fw={600} mb="md">
            {t("company:sectionHours")}
          </Text>
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4 sm:max-w-xs">
              <Input.Wrapper id="operatingHoursStart" label={t("company:operatingHoursStart")}>
                <Controller
                  control={control}
                  name="operatingHoursStart"
                  render={({ field }) => <TimePicker id="operatingHoursStart" disabled={!canManage} {...field} />}
                />
              </Input.Wrapper>
              <Input.Wrapper id="operatingHoursEnd" label={t("company:operatingHoursEnd")}>
                <Controller
                  control={control}
                  name="operatingHoursEnd"
                  render={({ field }) => <TimePicker id="operatingHoursEnd" disabled={!canManage} {...field} />}
                />
              </Input.Wrapper>
            </div>

            <Input.Wrapper label={t("company:workingDays")} labelElement="div">
              <Controller
                control={control}
                name="workingDays"
                render={({ field }) => (
                  <Group gap="xs" mt="xs">
                    <Chip.Group multiple value={field.value} onChange={field.onChange}>
                      {WEEK_DAYS.map((day) => (
                        <Chip key={day} value={day} disabled={!canManage}>
                          {t(`company:days.${day}`)}
                        </Chip>
                      ))}
                    </Chip.Group>
                  </Group>
                )}
              />
            </Input.Wrapper>
          </div>
        </Card>

        {canManage && (
          <Group justify="flex-end">
            <Button type="submit" disabled={!isDirty || saveMutation.isPending}>
              {saveMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        )}
      </form>

      {canViewIncentives && (
        <Card withBorder padding="lg">
          <Text fw={600} mb="xs">
            {t("company:sectionIncentives")}
          </Text>
          <Text size="sm" c="dimmed" mb="md">
            {t("company:incentivesHint")}
          </Text>
          {incentiveSummary && incentiveSummary.length > 0 ? (
            <Group gap="xs">
              {incentiveSummary.map((item) => (
                <Badge key={item.code} variant="light" size="lg">
                  {t("company:incentiveBadge", { code: item.code, count: item.employeeCount })}
                </Badge>
              ))}
            </Group>
          ) : (
            <Text size="sm" c="dimmed">
              {t("company:noActiveIncentives")}
            </Text>
          )}
        </Card>
      )}
    </div>
  );
}
