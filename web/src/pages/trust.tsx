import { useTranslation } from "react-i18next";
import { Card, Stack, Text, Title } from "@mantine/core";
import { KeyRound, Landmark, LifeBuoy, Lock, ScrollText, ShieldCheck } from "lucide-react";
import type { LucideIcon } from "lucide-react";

const SECTION_ICONS: Record<string, LucideIcon> = {
  encryption: Lock,
  tenantIsolation: Landmark,
  accessControl: ScrollText,
  supportAccess: LifeBuoy,
  continuity: KeyRound,
  compliance: ShieldCheck,
};

const SECTION_KEYS = Object.keys(SECTION_ICONS);

export default function TrustPage() {
  const { t } = useTranslation(["trust"]);

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <ShieldCheck className="h-6 w-6" /> {t("trust:title")}
        </Title>
        <Text c="dimmed">{t("trust:subtitle")}</Text>
      </div>

      <Text size="sm">{t("trust:intro")}</Text>

      <Stack gap="md">
        {SECTION_KEYS.map((key) => {
          const Icon = SECTION_ICONS[key];
          return (
            <Card key={key} withBorder padding="lg">
              <div className="flex items-start gap-3">
                <Icon className="mt-1 h-5 w-5 shrink-0" style={{ color: "var(--mantine-color-dimmed)" }} />
                <div>
                  <Title order={4} mb={4}>
                    {t(`trust:sections.${key}.title`)}
                  </Title>
                  <Text size="sm" c="dimmed">
                    {t(`trust:sections.${key}.body`)}
                  </Text>
                </div>
              </div>
            </Card>
          );
        })}
      </Stack>

      <Text size="sm" c="dimmed">
        {t("trust:contactCta")}
      </Text>
    </div>
  );
}
