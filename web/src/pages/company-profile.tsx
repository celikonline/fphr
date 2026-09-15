import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { FileDropzone } from "@/components/ui/file-dropzone";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { TimePicker } from "@/components/ui/time-picker";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { toast } from "@/hooks/use-toast";
import { usePermission } from "@/components/permission-guard";
import {
  getCompanyProfile,
  updateCompanyProfile,
  uploadCompanyLogo,
  type CompanyProfile,
  type WeekDay,
} from "@/services/company.service";

const LOGO_MAX_BYTES = 2 * 1024 * 1024;
const WEEK_DAYS: WeekDay[] =["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

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

  const { data: profile, isLoading } = useQuery({
    queryKey: ["company-profile"],
    queryFn: getCompanyProfile,
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

  if (isLoading || !profile) {
    return (
      <div className="space-y-4 p-6 md:p-10">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <Building2 className="h-6 w-6" /> {t("company:title")}
        </h1>
        <p className="text-muted-foreground">{t("company:subtitle")}</p>
      </div>

      {!canManage && (
        <Alert>
          <AlertDescription>{t("company:readOnlyNotice")}</AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("company:sectionLogo")}</CardTitle>
          <CardDescription>{t("company:logoHint")}</CardDescription>
        </CardHeader>
        <CardContent className="flex items-center gap-4">
          <Avatar className="h-16 w-16">
            <AvatarImage src={profile.logoUrl} alt={profile.name} />
            <AvatarFallback>{profile.name.slice(0, 2).toUpperCase()}</AvatarFallback>
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
            onReject={() => toast({ variant: "destructive", title: t("common:error"), description: t("company:logoRejected") })}
          />
        </CardContent>
      </Card>

      <form
        onSubmit={handleSubmit((values) => saveMutation.mutate(values))}
        className="space-y-6"
        aria-label={t("company:title")}
      >
        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t("company:sectionGeneral")}</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <Label htmlFor="name">{t("company:name")}</Label>
              <Input id="name" disabled={!canManage} {...register("name")} />
              {errors.name && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
            </div>
            <div>
              <Label htmlFor="registrationNumber">{t("company:registrationNumber")}</Label>
              <Input id="registrationNumber" disabled={!canManage} {...register("registrationNumber")} />
            </div>
            <div>
              <Label htmlFor="taxNumber">{t("company:taxNumber")}</Label>
              <Input id="taxNumber" disabled={!canManage} {...register("taxNumber")} />
            </div>
            <div className="sm:col-span-2">
              <Label htmlFor="address">{t("company:address")}</Label>
              <Input id="address" disabled={!canManage} {...register("address")} />
            </div>
            <div>
              <Label htmlFor="city">{t("company:city")}</Label>
              <Input id="city" disabled={!canManage} {...register("city")} />
            </div>
            <div>
              <Label htmlFor="country">{t("company:country")}</Label>
              <Input id="country" disabled={!canManage} {...register("country")} />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">{t("company:sectionHours")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 gap-4 sm:max-w-xs">
              <div>
                <Label htmlFor="operatingHoursStart">{t("company:operatingHoursStart")}</Label>
                <Controller
                  control={control}
                  name="operatingHoursStart"
                  render={({ field }) => <TimePicker id="operatingHoursStart" disabled={!canManage} {...field} />}
                />
              </div>
              <div>
                <Label htmlFor="operatingHoursEnd">{t("company:operatingHoursEnd")}</Label>
                <Controller
                  control={control}
                  name="operatingHoursEnd"
                  render={({ field }) => <TimePicker id="operatingHoursEnd" disabled={!canManage} {...field} />}
                />
              </div>
            </div>

            <div>
              <Label>{t("company:workingDays")}</Label>
              <Controller
                control={control}
                name="workingDays"
                render={({ field }) => (
                  <div className="mt-2 flex flex-wrap gap-3">
                    {WEEK_DAYS.map((day) => {
                      const checked = field.value.includes(day);
                      return (
                        <label key={day} className="flex items-center gap-1.5 text-sm">
                          <Checkbox
                            checked={checked}
                            disabled={!canManage}
                            onCheckedChange={(value) => {
                              field.onChange(
                                value ? [...field.value, day] : field.value.filter((d) => d !== day)
                              );
                            }}
                          />
                          {t(`company:days.${day}`)}
                        </label>
                      );
                    })}
                  </div>
                )}
              />
            </div>
          </CardContent>
        </Card>

        {canManage && (
          <div className="flex justify-end">
            <Button type="submit" disabled={!isDirty || saveMutation.isPending}>
              {saveMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </div>
        )}
      </form>
    </div>
  );
}
