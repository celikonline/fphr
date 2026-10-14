import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Alert, Button, Card, Code, CopyButton, Group, List, SimpleGrid, Stack, Text, Title, Tooltip } from "@mantine/core";
import { Check, Copy, QrCode } from "lucide-react";
import NoAccess from "@/components/no-access";
import { useAnyPermission } from "@/components/permission-guard";

const ENDPOINT = "/api/v1/attendance/terminals/integrations/{provider}";

/** PDKS cihazları için protokol köprüsü ve çalışan eşlemesi kurulum rehberi. */
export default function PdksIntegrationsPage() {
  const { t } = useTranslation(["terminals"]);
  const canView = useAnyPermission(["attendance.terminal.view", "attendance.terminal.manage"]);
  if (!canView) return <NoAccess />;

  const providers = ["zkteco", "hikvision", "suprema"] as const;

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <QrCode size={24} /> {t("terminals:integration.title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>{t("terminals:integration.subtitle")}</Text>
      </div>

      <Alert color="blue" title={t("terminals:integration.bridgeTitle")}>
        {t("terminals:integration.bridgeDescription")}
      </Alert>

      <Card withBorder padding="lg">
        <Stack gap="sm">
          <Text fw={600}>{t("terminals:integration.endpoint")}</Text>
          <Group wrap="nowrap" align="center">
            <Code block className="flex-1">POST {ENDPOINT}</Code>
            <CopyButton value={`POST ${ENDPOINT}`} timeout={1500}>
              {({ copied, copy }) => (
                <Tooltip label={copied ? t("terminals:integration.copied") : t("terminals:integration.copy")}>
                  <Button variant="default" aria-label={t("terminals:integration.copy")} onClick={copy}>
                    {copied ? <Check size={16} /> : <Copy size={16} />}
                  </Button>
                </Tooltip>
              )}
            </CopyButton>
          </Group>
          <Text size="sm" c="dimmed">{t("terminals:integration.authHint")}</Text>
        </Stack>
      </Card>

      <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
        {providers.map((provider) => (
          <Card key={provider} withBorder padding="lg">
            <Stack gap="sm">
              <Title order={2} size="h4">{t(`terminals:integration.providers.${provider}.name`)}</Title>
              <Text size="sm">{t(`terminals:integration.providers.${provider}.description`)}</Text>
              <Code block>{t(`terminals:integration.providers.${provider}.format`)}</Code>
            </Stack>
          </Card>
        ))}
      </SimpleGrid>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Title order={2} size="h4">{t("terminals:integration.setupTitle")}</Title>
          <List type="ordered" spacing="sm">
            <List.Item>{t("terminals:integration.steps.terminal")}</List.Item>
            <List.Item>{t("terminals:integration.steps.mapping")}</List.Item>
            <List.Item>{t("terminals:integration.steps.gateway")}</List.Item>
            <List.Item>{t("terminals:integration.steps.timezone")}</List.Item>
          </List>
          <Text size="sm">{t("terminals:integration.mappingLabel")} <Code>provider:terminal-code:device-user-id</Code></Text>
          <Text size="sm" c="dimmed">{t("terminals:integration.mappingExample")}</Text>
          <Group>
            <Button component={Link} to="/app/attendance/terminals" variant="light">
              {t("terminals:integration.manageTerminals")}
            </Button>
          </Group>
        </Stack>
      </Card>
    </div>
  );
}
