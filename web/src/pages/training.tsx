import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { GraduationCap } from "lucide-react";
import { Badge, Card, Group, Skeleton, Stack, Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { ENROLLMENT_STATUS_COLOR } from "@/components/training/training-program-detail";
import TrainingPrograms from "@/components/training/training-programs";
import { formatDate } from "@/lib/dates";
import { enumKey, getMyTrainings, type EnrollmentStatus } from "@/services/training.service";

/** Eğitim: çalışanın eğitimleri (training.my.enrollments) + İK program yönetimi (training.programs.view). */
export default function TrainingPage() {
  const canMine = usePermission("training.my.enrollments");
  const canPrograms = usePermission("training.programs.view");
  if (!canMine && !canPrograms) return <NoAccess />;
  return <TrainingScreen canMine={canMine} canPrograms={canPrograms} />;
}

function TrainingScreen({ canMine, canPrograms }: { canMine: boolean; canPrograms: boolean }) {
  const { t } = useTranslation(["training"]);

  return (
    <Stack gap="md" className="p-6 md:p-10">
      <Title order={2}>{t("training:title")}</Title>
      {canMine && canPrograms ? (
        <Tabs defaultValue="programs" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="programs">{t("training:tabs.programs")}</Tabs.Tab>
            <Tabs.Tab value="mine">{t("training:tabs.mine")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="programs" pt="md">
            <TrainingPrograms />
          </Tabs.Panel>
          <Tabs.Panel value="mine" pt="md">
            <MyTrainings />
          </Tabs.Panel>
        </Tabs>
      ) : canPrograms ? (
        <TrainingPrograms />
      ) : (
        <MyTrainings />
      )}
    </Stack>
  );
}

function MyTrainings() {
  const { t, i18n } = useTranslation(["training"]);
  const query = useQuery({ queryKey: ["training", "my"], queryFn: getMyTrainings });
  const items = query.data ?? [];

  if (query.isLoading) return <Skeleton height={120} />;
  if (query.isError) return <Text c="red">{t("training:loadError")}</Text>;
  if (items.length === 0) {
    return (
      <Card withBorder>
        <Group gap="sm">
          <GraduationCap size={18} />
          <Text c="dimmed">{t("training:mine.empty")}</Text>
        </Group>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {items.map((item) => {
        const status = enumKey<EnrollmentStatus>(item.status);
        const dates = [item.startDate, item.endDate]
          .filter((value): value is string => Boolean(value))
          .map((value) => formatDate(value, i18n.language))
          .join(" – ");
        return (
          <Card key={item.enrollmentId} withBorder padding="md">
            <Group justify="space-between" wrap="nowrap" align="flex-start">
              <Stack gap={4}>
                <Text fw={600}>{item.title}</Text>
                <Text size="xs" c="dimmed">
                  {t(`training:categories.${enumKey(item.category)}`)} · {dates}
                </Text>
                {item.description && <Text size="sm">{item.description}</Text>}
                {item.certificateNumber && (
                  <Text size="sm">
                    {t("training:participants.certificate")}: {item.certificateNumber}
                  </Text>
                )}
              </Stack>
              <Badge color={ENROLLMENT_STATUS_COLOR[status] ?? "gray"} variant="light">
                {t(`training:enrollmentStatuses.${status}`)}
              </Badge>
            </Group>
          </Card>
        );
      })}
    </Stack>
  );
}
