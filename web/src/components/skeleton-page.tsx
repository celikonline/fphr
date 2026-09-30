import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Card, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { Hourglass } from "lucide-react";
import NoAccess from "@/components/no-access";
import { useAnyPermission } from "@/components/permission-guard";

interface SkeletonPageProps {
  /** i18n namespace holding `<titleKey>`, `<subtitleKey>` and a shared `comingSoon` string. */
  ns: string;
  titleKey: string;
  subtitleKey: string;
  /** Any of these permissions opens the page. */
  permissions: readonly string[];
  icon?: ReactNode;
}

/** Empty shell for zaman-devam screens that ship in a later lane: page header + "coming soon" empty state. */
export default function SkeletonPage({ ns, titleKey, subtitleKey, permissions, icon }: SkeletonPageProps) {
  const { t } = useTranslation([ns]);
  const allowed = useAnyPermission([...permissions]);
  if (!allowed) return <NoAccess />;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          {icon} {t(`${ns}:${titleKey}`)}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t(`${ns}:${subtitleKey}`)}
        </Text>
      </div>
      <Card withBorder padding="xl" radius="md">
        <Stack align="center" gap="xs" className="min-h-40" justify="center">
          <ThemeIcon variant="light" color="gray" size={40} radius="xl">
            <Hourglass size={20} />
          </ThemeIcon>
          <Text c="dimmed" size="sm" ta="center">
            {t(`${ns}:comingSoon`)}
          </Text>
        </Stack>
      </Card>
    </div>
  );
}
