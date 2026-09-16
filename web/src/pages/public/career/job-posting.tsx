import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Briefcase, CheckCircle2, Clock } from "lucide-react";
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Card,
  Center,
  Checkbox,
  Container,
  Divider,
  Group,
  Loader,
  Modal,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { getApiProblem } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  getPublicJobPosting,
  listRelatedPublicJobPostings,
  submitCareerApplication,
  type PublicJobPostingSummary,
} from "@/services/recruitment.service";
import classes from "./job-posting.module.css";

const schema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
  consent: z.boolean().refine((v) => v === true, { message: "required" }),
});

type FormValues = z.infer<typeof schema>;

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className={classes.page}>
      <Container size="sm" py={{ base: 24, sm: 48 }} px="md">
        {children}
      </Container>
    </div>
  );
}

function NotFoundCard() {
  const { t } = useTranslation(["recruitment"]);
  return (
    <Shell>
      <Card withBorder padding="xl" radius="md">
        <Stack align="center" ta="center" gap="xs">
          <Clock size={40} color="var(--mantine-color-yellow-6)" />
          <Title order={3}>{t("recruitment:career.notFoundTitle")}</Title>
          <Text size="sm" c="dimmed">
            {t("recruitment:career.notFoundText")}
          </Text>
        </Stack>
      </Card>
    </Shell>
  );
}

function RelatedPostings({ items }: { items: PublicJobPostingSummary[] }) {
  const { t } = useTranslation(["recruitment"]);
  if (items.length === 0) return null;
  return (
    <Card withBorder padding="lg" radius="md">
      <Stack gap="sm">
        <Title order={4}>{t("recruitment:career.otherOpenPositions")}</Title>
        <Stack gap={6}>
          {items.map((item) => (
            <Group key={item.applySlug} justify="space-between" wrap="nowrap">
              <Text
                component={Link}
                to={`/kariyer/${item.applySlug}`}
                size="sm"
                fw={500}
                className={classes.relatedLink}
              >
                {item.title}
              </Text>
              <Badge variant="light" size="sm">
                {t(`recruitment:employmentTypes.${item.employmentType}`)}
              </Badge>
            </Group>
          ))}
        </Stack>
      </Stack>
    </Card>
  );
}

/**
 * TALIMAT tur 1 FE madde 2: KVKK onay kutusunun yanında aydınlatma metnini gösteren modal.
 * Metin `recruitment:career.privacyNotice.body` anahtarından, versiyon `recruitment:career.privacyNoticeVersion`'dan.
 */
function PrivacyNoticeLink() {
  const { t } = useTranslation(["recruitment"]);
  const [opened, setOpened] = useState(false);
  return (
    <>
      <Anchor component="button" type="button" size="sm" onClick={() => setOpened(true)}>
        {t("recruitment:career.privacyNotice.readLink")}
      </Anchor>
      <Modal opened={opened} onClose={() => setOpened(false)} title={t("recruitment:career.privacyNotice.title")}>
        <Stack gap="sm">
          <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
            {t("recruitment:career.privacyNotice.body")}
          </Text>
          <Text size="xs" c="dimmed">
            {t("recruitment:career.privacyNoticeVersion")}
          </Text>
          <Button variant="default" onClick={() => setOpened(false)}>
            {t("recruitment:career.privacyNotice.close")}
          </Button>
        </Stack>
      </Modal>
    </>
  );
}

function SuccessCard() {
  const { t } = useTranslation(["recruitment"]);
  return (
    <Card withBorder padding="xl" radius="md">
      <Stack align="center" ta="center" gap="xs">
        <CheckCircle2 size={40} color="var(--mantine-color-green-7)" />
        <Title order={3}>{t("recruitment:career.submitSuccessTitle")}</Title>
        <Text size="sm" c="dimmed">
          {t("recruitment:career.submitSuccessText")}
        </Text>
      </Stack>
    </Card>
  );
}

function ApplicationForm({ applySlug }: { applySlug: string }) {
  const { t } = useTranslation(["recruitment", "common"]);
  const [alreadyApplied, setAlreadyApplied] = useState(false);
  const [privacyNoticeOutdated, setPrivacyNoticeOutdated] = useState(false);
  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: "", lastName: "", email: "", phone: "", consent: false },
  });

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      submitCareerApplication(applySlug, {
        firstName: values.firstName,
        lastName: values.lastName,
        email: values.email,
        phone: values.phone || null,
        // Faz 1'in IConsentRecorder'ı gelene kadar aydınlatma metni versiyonu başvuruyla taşınır (bkz. TALIMAT tur 3).
        privacyNoticeVersion: t("recruitment:career.privacyNoticeVersion"),
        kvkkAccepted: values.consent,
      }),
    onError: (err) => {
      const code = getApiProblem(err)?.code;
      if (code === "recruitment.application.already_exists") {
        setAlreadyApplied(true);
      } else if (code === "recruitment.application.privacy_notice_outdated") {
        setPrivacyNoticeOutdated(true);
      }
    },
  });

  if (mutation.isSuccess) return <SuccessCard />;

  return (
    <Card withBorder padding="lg" radius="md">
      <form onSubmit={handleSubmit((values) => mutation.mutate(values))} noValidate>
        <Stack gap="sm">
          <Title order={4}>{t("recruitment:career.applyTitle")}</Title>

          {alreadyApplied && (
            <Alert color="yellow" variant="light">
              {t("recruitment:career.alreadyApplied")}
            </Alert>
          )}
          {privacyNoticeOutdated && (
            <Alert color="yellow" variant="light">
              {t("recruitment:career.privacyNoticeOutdated")}
            </Alert>
          )}
          {mutation.isError && !alreadyApplied && !privacyNoticeOutdated && (
            <Alert color="red" variant="light">
              {t("recruitment:genericError")}
            </Alert>
          )}

          <TextInput
            label={t("recruitment:career.firstName")}
            withAsterisk
            disabled={mutation.isPending}
            error={errors.firstName && t("common:required")}
            {...register("firstName")}
          />
          <TextInput
            label={t("recruitment:career.lastName")}
            withAsterisk
            disabled={mutation.isPending}
            error={errors.lastName && t("common:required")}
            {...register("lastName")}
          />
          <TextInput
            label={t("recruitment:career.email")}
            type="email"
            withAsterisk
            disabled={mutation.isPending}
            error={errors.email && t("common:required")}
            {...register("email")}
          />
          <TextInput
            label={t("recruitment:career.phoneOptional")}
            disabled={mutation.isPending}
            {...register("phone")}
          />

          <Controller
            control={control}
            name="consent"
            render={({ field }) => (
              <Checkbox
                label={t("recruitment:career.consentText")}
                checked={field.value === true}
                onChange={(e) => field.onChange(e.currentTarget.checked)}
                disabled={mutation.isPending}
                error={errors.consent && t("recruitment:career.consentRequired")}
              />
            )}
          />
          <PrivacyNoticeLink />

          <Button type="submit" loading={mutation.isPending} fullWidth>
            {mutation.isPending ? t("recruitment:career.submitting") : t("recruitment:career.submit")}
          </Button>
        </Stack>
      </form>
    </Card>
  );
}

export default function CareerJobPostingPage() {
  const { t, i18n } = useTranslation(["recruitment"]);
  const { applySlug = "" } = useParams();

  const postingQuery = useQuery({
    queryKey: ["public", "recruitment", "posting", applySlug],
    queryFn: () => getPublicJobPosting(applySlug),
    enabled: !!applySlug,
    retry: false,
  });
  const relatedQuery = useQuery({
    queryKey: ["public", "recruitment", "posting", applySlug, "related"],
    queryFn: () => listRelatedPublicJobPostings(applySlug),
    enabled: postingQuery.isSuccess,
  });

  useEffect(() => {
    if (postingQuery.data?.title) {
      document.title = `${postingQuery.data.title} · ${t("recruitment:career.list.pageTitleSuffix")}`;
    }
    return () => {
      document.title = "SenseİK - İK Platformu";
    };
  }, [postingQuery.data?.title, t]);

  if (!applySlug || postingQuery.isError) return <NotFoundCard />;

  if (postingQuery.isLoading || !postingQuery.data) {
    return (
      <Shell>
        <Center mih={200}>
          <Loader />
        </Center>
      </Shell>
    );
  }

  const posting = postingQuery.data;

  return (
    <Shell>
      <Stack gap="lg">
        <Card withBorder padding="lg" radius="md">
          <Stack gap="xs">
            <Group gap="xs">
              <Briefcase size={20} />
              <Badge variant="light">{t(`recruitment:employmentTypes.${posting.employmentType}`)}</Badge>
            </Group>
            <Title order={2}>{posting.title}</Title>
            <Group gap="md" c="dimmed">
              {posting.closingDate && (
                <Text size="sm">
                  {t("recruitment:career.closingDate")}: {formatDateOnly(posting.closingDate, i18n.language)}
                </Text>
              )}
              {posting.publishedAt && (
                <Text size="sm">
                  {t("recruitment:career.publishedAt")}: {formatDateTime(posting.publishedAt, i18n.language)}
                </Text>
              )}
            </Group>
            {posting.description && (
              <>
                <Divider my="xs" />
                <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
                  {posting.description}
                </Text>
              </>
            )}
          </Stack>
        </Card>

        <ApplicationForm applySlug={applySlug} />

        {relatedQuery.data && <RelatedPostings items={relatedQuery.data} />}
      </Stack>
    </Shell>
  );
}
