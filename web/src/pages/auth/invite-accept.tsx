import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Card,
  Center,
  Checkbox,
  Group,
  Modal,
  PasswordInput,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { AlertTriangle, MailCheck } from "lucide-react";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus, getApiProblem } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { acceptInvitation, getInvitationPreview } from "@/services/invitations.service";
import classes from "./invite-accept.module.css";

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
    <Center mih="100vh" px="md" py={48} bg="var(--mantine-color-body)">
      <Card withBorder padding="xl" w="100%" maw={448} className={classes.card}>
        {children}
      </Card>
    </Center>
  );
}

/**
 * KVKK aydınlatma metni bağlantısı + modalı (TALIMAT tur 1 FE madde 1). Kalıp `pages/public/career/job-posting.tsx`
 * içindeki `PrivacyNoticeLink`'in aynısı — bileşen oradan import edilmiyor, bu sayfaya özgü kopyalanmış hâli.
 * Modal kapanınca onay kutusunu otomatik işaretlemiyoruz; kullanıcı okuyup kendisi işaretler.
 *
 * TALIMAT tur 6 açılışı (ADR-0026): metnin hukuk/İK onayı henüz gelmedi (🧑 H-4), bu yüzden metin **taslak**
 * etiketiyle ve versiyonuyla gösterilir — onay gelince versiyon artar, eski rızalar geçersizleşir. Rıza
 * gönderiminde versiyon alanı (`privacyNoticeVersion`, bkz. `invitations.service.ts`) zaten her zaman dolu
 * gönderiliyor (sabit `auth:privacy.noticeVersion`), bu turda değişmedi.
 */
function PrivacyNoticeLink() {
  const { t } = useTranslation(["auth", "common"]);
  const [opened, setOpened] = useState(false);
  return (
    <>
      <Anchor component="button" type="button" size="sm" onClick={() => setOpened(true)}>
        {t("auth:privacy.readNotice")}
      </Anchor>
      <Modal
        opened={opened}
        onClose={() => setOpened(false)}
        title={
          <Group gap="xs" wrap="nowrap">
            <Text fw={600}>{t("auth:privacy.title")}</Text>
            <Badge color="yellow" variant="light" size="sm">
              {t("auth:privacy.draftBadge")}
            </Badge>
          </Group>
        }
      >
        <Stack gap="sm">
          <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
            {t("auth:privacy.body")}
          </Text>
          <Text size="xs" c="dimmed">
            {t("auth:privacy.versionLabel", { version: t("auth:privacy.noticeVersion") })}
          </Text>
          <Text size="xs" c="dimmed">
            {t("auth:privacy.draftNotice")}
          </Text>
          <Button variant="default" onClick={() => setOpened(false)}>
            {t("common:close")}
          </Button>
        </Stack>
      </Modal>
    </>
  );
}

function InvalidInvitation({ reason }: { reason: "invalid" | "unusable" | "error" }) {
  const { t } = useTranslation(["auth"]);
  return (
    <CenteredCard>
      <Stack align="center" ta="center" gap="xs">
        <AlertTriangle size={40} color="var(--mantine-color-yellow-6)" />
        <Title order={3}>{t(`auth:invite.${reason}Title`)}</Title>
        <Text size="sm" c="dimmed">
          {t(`auth:invite.${reason}Description`)}
        </Text>
        <Button component={Link} to="/login" variant="default" mt="md">
          {t("auth:invite.goToLogin")}
        </Button>
      </Stack>
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
      acceptInvitation({
        token,
        password: values.password,
        privacyNoticeAccepted: values.privacyNoticeAccepted,
        privacyNoticeVersion: t("auth:privacy.noticeVersion"),
      }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("auth:invite.acceptSuccess") });
      navigate("/login", { replace: true });
    },
    onError: (error) => {
      // S-12 ekindeki BE sözleşmesi (G-3-05): KVKK sabiti güncellendiğinde eski versiyonla gönderilen kabul
      // reddedilir. Backend `detail`'i zaten yerelleştirilmiş metni taşır ama burada özel kodu yakalayıp
      // `auth:privacy.outdated` üzerinden göstermek, i18n içeriğini FE tarafında da tek yerde tutuyor.
      const code = getApiProblem(error)?.code;
      setSubmitError(
        code === "identity.invitation.privacy_notice_outdated"
          ? t("auth:privacy.outdated")
          : getApiErrorMessage(error, t("auth:invite.acceptError")),
      );
    },
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
        <Stack gap="sm">
          <Skeleton h={32} w="66%" />
          <Skeleton h={16} />
          <Skeleton h={40} />
          <Skeleton h={40} />
        </Stack>
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
      <Stack gap="xs" mb="lg">
        <MailCheck size={32} color="var(--mantine-primary-color-filled)" />
        <Title order={3}>{t("auth:invite.title", { tenant: invitation.tenantName })}</Title>
        <Text size="sm" c="dimmed">
          {t("auth:invite.subtitle", { name: `${invitation.firstName} ${invitation.lastName}`.trim() })}
        </Text>
      </Stack>
      <form
        onSubmit={handleSubmit((values) => {
          setSubmitError(null);
          acceptMutation.mutate(values);
        })}
      >
        <Stack gap="md">
          {submitError && (
            <Alert color="red" variant="light">
              {submitError}
            </Alert>
          )}

          <TextInput id="invite-email" label={t("auth:email")} value={invitation.email} readOnly disabled />
          <PasswordInput
            id="invite-password"
            label={t("auth:password")}
            autoComplete="new-password"
            inputWrapperOrder={["label", "input", "description", "error"]}
            description={errors.password ? undefined : t("auth:invite.passwordHint", { min: MIN_PASSWORD_LENGTH })}
            error={errorText(errors.password?.message)}
            {...register("password")}
          />
          <PasswordInput
            id="invite-confirm"
            label={t("auth:invite.confirmPassword")}
            autoComplete="new-password"
            error={errorText(errors.confirmPassword?.message)}
            {...register("confirmPassword")}
          />
          <Checkbox
            id="invite-privacy"
            checked={privacyAccepted}
            onChange={(event) =>
              setValue("privacyNoticeAccepted", event.currentTarget.checked, { shouldValidate: true })
            }
            label={t("auth:invite.privacyNotice")}
            error={errorText(errors.privacyNoticeAccepted?.message)}
          />
          <PrivacyNoticeLink />

          <Button type="submit" fullWidth disabled={acceptMutation.isPending}>
            {acceptMutation.isPending ? t("auth:invite.accepting") : t("auth:invite.accept")}
          </Button>
          <Text size="xs" c="dimmed" ta="center">
            {t("auth:invite.expiresAt", { date: formatDateTime(invitation.expiresAt, i18n.language) })}
          </Text>
        </Stack>
      </form>
    </CenteredCard>
  );
}
