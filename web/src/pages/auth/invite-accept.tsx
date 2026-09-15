import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, MailCheck } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { acceptInvitation, getInvitationPreview } from "@/services/invitations.service";

/** Mirrors IdentityDefaults.MinPasswordLength; the server validates the configured value. */
const MIN_PASSWORD_LENGTH = 10;

const schema = z
  .object({
    password: z.string().min(MIN_PASSWORD_LENGTH, "passwordTooShort"),
    confirmPassword: z.string(),
    privacyNoticeAccepted: z.boolean().refine((value) => value, "privacyRequired"),
  })
  .refine((values) => values.password === values.confirmPassword, {
    path: ["confirmPassword"],
    message: "passwordMismatch",
  });

type FormValues = z.infer<typeof schema>;

function CenteredCard({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/40 px-4 py-12">
      <Card className="w-full max-w-md">{children}</Card>
    </div>
  );
}

function InvalidInvitation({ reason }: { reason: "invalid" | "unusable" | "error" }) {
  const { t } = useTranslation(["auth"]);
  return (
    <CenteredCard>
      <CardHeader className="items-center text-center">
        <AlertTriangle className="mb-2 h-10 w-10 text-amber-500" />
        <CardTitle>{t(`auth:invite.${reason}Title`)}</CardTitle>
        <CardDescription>{t(`auth:invite.${reason}Description`)}</CardDescription>
      </CardHeader>
      <CardContent className="flex justify-center">
        <Button asChild variant="outline">
          <Link to="/login">{t("auth:invite.goToLogin")}</Link>
        </Button>
      </CardContent>
    </CenteredCard>
  );
}

export default function InviteAcceptPage() {
  const { t, i18n } = useTranslation(["auth", "common"]);
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const [submitError, setSubmitError] = useState<string | null>(null);

  const preview = useQuery({
    queryKey: ["invitation-preview", token],
    queryFn: () => getInvitationPreview(token),
    enabled: token.length > 0,
    retry: false,
  });

  const {
    register,
    handleSubmit,
    setValue,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { password: "", confirmPassword: "", privacyNoticeAccepted: false },
  });

  const privacyAccepted = useWatch({ control, name: "privacyNoticeAccepted" });

  const acceptMutation = useMutation({
    mutationFn: (values: FormValues) =>
      acceptInvitation({ token, password: values.password, privacyNoticeAccepted: values.privacyNoticeAccepted }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("auth:invite.acceptSuccess") });
      navigate("/login", { replace: true });
    },
    onError: (error) => setSubmitError(getApiErrorMessage(error, t("auth:invite.acceptError"))),
  });

  if (!token || (preview.isError && [400, 404].includes(getApiErrorStatus(preview.error) ?? 0))) {
    return <InvalidInvitation reason="invalid" />;
  }
  if (preview.isError) {
    return <InvalidInvitation reason="error" />;
  }
  if (preview.isLoading || !preview.data) {
    return (
      <CenteredCard>
        <CardContent className="space-y-3 p-6">
          <Skeleton className="h-8 w-2/3" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </CardContent>
      </CenteredCard>
    );
  }
  if (!preview.data.isUsable) {
    return <InvalidInvitation reason="unusable" />;
  }

  const invitation = preview.data;
  const errorText = (code?: string) => (code ? t(`auth:invite.errors.${code}`, { min: MIN_PASSWORD_LENGTH }) : null);

  return (
    <CenteredCard>
      <CardHeader>
        <MailCheck className="mb-2 h-8 w-8 text-primary" />
        <CardTitle>{t("auth:invite.title", { tenant: invitation.tenantName })}</CardTitle>
        <CardDescription>
          {t("auth:invite.subtitle", { name: `${invitation.firstName} ${invitation.lastName}`.trim() })}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="space-y-4"
          onSubmit={handleSubmit((values) => {
            setSubmitError(null);
            acceptMutation.mutate(values);
          })}
        >
          {submitError && (
            <Alert variant="destructive">
              <AlertDescription>{submitError}</AlertDescription>
            </Alert>
          )}

          <div>
            <Label htmlFor="invite-email">{t("auth:email")}</Label>
            <Input id="invite-email" value={invitation.email} readOnly disabled />
          </div>
          <div>
            <Label htmlFor="invite-password">{t("auth:password")}</Label>
            <Input id="invite-password" type="password" autoComplete="new-password" {...register("password")} />
            <p className={errors.password ? "mt-1 text-xs text-destructive" : "mt-1 text-xs text-muted-foreground"}>
              {errorText(errors.password?.message) ?? t("auth:invite.passwordHint", { min: MIN_PASSWORD_LENGTH })}
            </p>
          </div>
          <div>
            <Label htmlFor="invite-confirm">{t("auth:invite.confirmPassword")}</Label>
            <Input id="invite-confirm" type="password" autoComplete="new-password" {...register("confirmPassword")} />
            {errors.confirmPassword && (
              <p className="mt-1 text-xs text-destructive">{errorText(errors.confirmPassword.message)}</p>
            )}
          </div>
          <div>
            <div className="flex items-start gap-2">
              <Checkbox
                id="invite-privacy"
                checked={privacyAccepted}
                onCheckedChange={(checked) => setValue("privacyNoticeAccepted", checked === true, { shouldValidate: true })}
              />
              <Label htmlFor="invite-privacy" className="text-sm font-normal leading-snug">
                {t("auth:invite.privacyNotice")}
              </Label>
            </div>
            {errors.privacyNoticeAccepted && (
              <p className="mt-1 text-xs text-destructive">{errorText(errors.privacyNoticeAccepted.message)}</p>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={acceptMutation.isPending}>
            {acceptMutation.isPending ? t("auth:invite.accepting") : t("auth:invite.accept")}
          </Button>
          <p className="text-center text-xs text-muted-foreground">
            {t("auth:invite.expiresAt", { date: formatDateTime(invitation.expiresAt, i18n.language) })}
          </p>
        </form>
      </CardContent>
    </CenteredCard>
  );
}
