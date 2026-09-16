import { Link, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Briefcase, MapPin } from "lucide-react";
import { Badge, Card, Center, Container, Group, Loader, Stack, Text, Title } from "@mantine/core";
import { formatDateOnly } from "@/lib/dates";
import { listPublicJobPostingsByTenant, type PublicJobPostingListItem } from "@/services/recruitment.service";
import classes from "./job-posting.module.css";
import listClasses from "./postings.module.css";

/**
 * Public "kariyer" listesi (`/kariyer?tenant=<slug>`), TALIMAT tur 3 FE madde 2. BE sözleşmesi S-10:
 * `GET /api/v1/recruitment/public/postings?tenant=` — kiracı bulunamazsa boş dizi döner (404 değil, kiracı
 * var/yok bilgisi sızmaz), bu yüzden burada da "tenant yok" ile "açık pozisyon yok" aynı boş durumla gösterilir.
 */
function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className={classes.page}>
      <Container size="sm" py={{ base: 24, sm: 48 }} px="md">
        {children}
      </Container>
    </div>
  );
}

function PostingCard({ item }: { item: PublicJobPostingListItem }) {
  const { t, i18n } = useTranslation(["recruitment"]);
  return (
    <Card
      component={Link}
      to={`/kariyer/${item.applySlug}`}
      withBorder
      padding="lg"
      radius="md"
      className={listClasses.postingCard}
    >
      <Stack gap={6}>
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Title order={4}>{item.title}</Title>
          {item.employmentType && (
            <Badge variant="light" size="sm">
              {t(`recruitment:employmentTypes.${item.employmentType}`)}
            </Badge>
          )}
        </Group>
        <Group gap="md" c="dimmed">
          <Group gap={4}>
            <MapPin size={14} />
            <Text size="sm">{item.location || t("recruitment:career.list.locationUnspecified")}</Text>
          </Group>
          {item.publishedAt && (
            <Text size="sm">
              {t("recruitment:career.publishedAt")}: {formatDateOnly(item.publishedAt, i18n.language)}
            </Text>
          )}
        </Group>
        <Text size="sm" fw={500} c="blue.7">
          {t("recruitment:career.list.viewPosting")}
        </Text>
      </Stack>
    </Card>
  );
}

export default function CareerPostingsListPage() {
  const { t } = useTranslation(["recruitment"]);
  const [searchParams] = useSearchParams();
  const tenant = searchParams.get("tenant") ?? "";

  const postingsQuery = useQuery({
    queryKey: ["public", "recruitment", "postings", tenant],
    queryFn: () => listPublicJobPostingsByTenant(tenant),
    enabled: !!tenant,
  });

  return (
    <Shell>
      <Stack gap="lg">
        <Stack gap={4}>
          <Group gap="xs">
            <Briefcase size={22} />
            <Title order={2}>{t("recruitment:career.list.title")}</Title>
          </Group>
          <Text c="dimmed" size="sm">
            {t("recruitment:career.list.subtitle")}
          </Text>
        </Stack>

        {!tenant || postingsQuery.isLoading ? (
          tenant ? (
            <Center mih={160}>
              <Loader />
            </Center>
          ) : (
            <Card withBorder padding="lg" radius="md">
              <Text size="sm" c="dimmed">
                {t("recruitment:career.list.empty")}
              </Text>
            </Card>
          )
        ) : postingsQuery.isError ? (
          <Card withBorder padding="lg" radius="md">
            <Text size="sm" c="red">
              {t("recruitment:career.list.loadError")}
            </Text>
          </Card>
        ) : !postingsQuery.data || postingsQuery.data.length === 0 ? (
          <Card withBorder padding="lg" radius="md">
            <Text size="sm" c="dimmed">
              {t("recruitment:career.list.empty")}
            </Text>
          </Card>
        ) : (
          <Stack gap="sm">
            {postingsQuery.data.map((item) => (
              <PostingCard key={item.applySlug} item={item} />
            ))}
          </Stack>
        )}
      </Stack>
    </Shell>
  );
}
