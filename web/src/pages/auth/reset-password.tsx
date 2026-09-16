import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Alert, Button, Card, Center, PasswordInput, Stack, Text, Title } from "@mantine/core";
import { KeyRound } from "lucide-react";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { resetPassword } from "@/services/password-reset.service";
import classes from "./auth-card.module.css";

/** Mirrors IdentityDefaults.MinPasswordLength; the server validates the configured value. */
const MIN_PASSWORD_LENGTH = 10;

const schema = z
  .object({
    newPassword: z.string().min(MIN_PASSWORD_LENGTH, "passwordTooShort"),
    confirmPassword: z.string(),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    path: ["confirmPassword"],
    message: "passwordMismatch",
  });

type FormValues = z.infer<typeof schema>;

export default function ResetPasswordPage() {
  const { t } = useTranslation(["auth", "common"]);
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { newPassword: "", confirmPassword: "" },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => resetPassword({ token, newPassword: values.newPassword }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("auth:resetPasswordPage.success") });
      navigate("/login", { replace: true });
    },
    onError: (error) => {
      const status = getApiErrorStatus(error);
      if (status === 404 || status === 422) {
        setExpired(true);
        return;
      }
      // `[EnableRateLimiting("public-anonymous-write")]` (ADR-0003) rejects with a bare 429, no ProblemDetails body.
      const fallback = status === 429 ? t("auth:resetPasswordPage.tooManyRequests") : t("auth:resetPasswordPage.error");
      setSubmitError(getApiErrorMessage(error, fallback));
    },
  });

  const errorText = (code?: string) => (code ? t(`auth:resetPasswordPage.errors.${code}`, { min: MIN_PASSWORD_LENGTH }) : null);

  return (
    <Center mih="100vh" px="md" py={48} bg="var(--mantine-color-default-hover)">
      <Card withBorder padding="xl" w="100%" maw={448} className={classes.card}>
        {!token || expired ? (
          <Stack align="center" ta="center" gap="xs">
            <Title order={3}>{t("auth:resetPasswordPage.expiredTitle")}</Title>
            <Text size="sm" c="dimmed">
              {t("auth:resetPasswordPage.expiredDescription")}
            </Text>
            <Button component={Link} to="/forgot-password" mt="md">
              {t("auth:resetPasswordPage.requestNewLink")}
            </Button>
          </Stack>
        ) : (
          <>
            <Stack gap="xs" mb="lg">
              <KeyRound size={32} color="var(--mantine-primary-color-filled)" />
              <Title order={3}>{t("auth:resetPasswordPage.title")}</Title>
              <Text size="sm" c="dimmed">
                {t("auth:resetPasswordPage.subtitle")}
              </Text>
            </Stack>
            <form
              onSubmit={handleSubmit((values) => {
                setSubmitError(null);
                mutation.mutate(values);
              })}
            >
              <Stack gap="md">
                {submitError && (
                  <Alert color="red" variant="light">
                    {submitError}
                  </Alert>
                )}

                <PasswordInput
                  id="reset-password-new"
                  label={t("auth:resetPasswordPage.newPassword")}
                  autoComplete="new-password"
                  inputWrapperOrder={["label", "input", "description", "error"]}
                  description={errors.newPassword ? undefined : t("auth:resetPasswordPage.passwordHint", { min: MIN_PASSWORD_LENGTH })}
                  error={errorText(errors.newPassword?.message)}
                  {...register("newPassword")}
                />
                <PasswordInput
                  id="reset-password-confirm"
                  label={t("auth:resetPasswordPage.confirmPassword")}
                  autoComplete="new-password"
                  error={errorText(errors.confirmPassword?.message)}
                  {...register("confirmPassword")}
                />

                <Button type="submit" fullWidth disabled={mutation.isPending}>
                  {mutation.isPending ? t("auth:resetPasswordPage.submitting") : t("auth:resetPasswordPage.submit")}
                </Button>
              </Stack>
            </form>
          </>
        )}
      </Card>
    </Center>
  );
}
