import { useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Alert, Button, Card, Center, Stack, Text, TextInput, Title } from "@mantine/core";
import { ArrowLeft, MailCheck } from "lucide-react";
import { getApiErrorMessage } from "@/lib/api-error";
import { forgotPassword } from "@/services/password-reset.service";
import classes from "./auth-card.module.css";

const schema = z.object({
  email: z.string().min(1).email(),
});

type FormValues = z.infer<typeof schema>;

export default function ForgotPasswordPage() {
  const { t } = useTranslation(["auth", "common"]);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "" },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) => forgotPassword(values.email),
    onError: (error) => setSubmitError(getApiErrorMessage(error, t("auth:forgotPasswordPage.error"))),
  });

  return (
    <Center mih="100vh" px="md" py={48} bg="var(--mantine-color-default-hover)">
      <Card withBorder padding="xl" w="100%" maw={448} className={classes.card}>
        {mutation.isSuccess ? (
          <Stack align="center" ta="center" gap="xs">
            <MailCheck size={40} color="var(--mantine-primary-color-filled)" />
            <Title order={3}>{t("auth:forgotPasswordPage.successTitle")}</Title>
            <Text size="sm" c="dimmed">
              {t("auth:forgotPasswordPage.successDescription")}
            </Text>
            <Button component={Link} to="/login" variant="default" mt="md">
              {t("auth:forgotPasswordPage.backToLogin")}
            </Button>
          </Stack>
        ) : (
          <>
            <Stack gap="xs" mb="lg">
              <Title order={3}>{t("auth:forgotPasswordPage.title")}</Title>
              <Text size="sm" c="dimmed">
                {t("auth:forgotPasswordPage.subtitle")}
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

                <TextInput
                  id="forgot-password-email"
                  type="email"
                  label={t("auth:email")}
                  placeholder={t("auth:emailPlaceholder")}
                  autoComplete="username"
                  error={errors.email ? t("auth:forgotPasswordPage.errors.invalid") : null}
                  {...register("email")}
                />

                <Button type="submit" fullWidth disabled={mutation.isPending}>
                  {mutation.isPending ? t("auth:forgotPasswordPage.submitting") : t("auth:forgotPasswordPage.submit")}
                </Button>

                <Button component={Link} to="/login" variant="subtle" fullWidth leftSection={<ArrowLeft size={16} />}>
                  {t("auth:forgotPasswordPage.backToLogin")}
                </Button>
              </Stack>
            </form>
          </>
        )}
      </Card>
    </Center>
  );
}
