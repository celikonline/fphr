import { useTranslation } from "react-i18next";
import { Stack, Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import ComposeTab from "./compose";
import GroupsTab from "./groups";
import InboxTab from "./inbox";
import OutboxTab from "./outbox";

export default function MessagesPage() {
  const { t } = useTranslation("messages");
  const canView = usePermission("notifications.messages.view");
  const canSend = usePermission("notifications.messages.send");
  const canGroups = usePermission("notifications.messagegroups.manage");
  if (!canView) return <NoAccess />;

  return (
    <Stack gap="md" className="w-full min-w-0 p-4 md:p-6">
      <div>
        <Title order={2}>{t("title")}</Title>
        <Text c="dimmed">{t("subtitle")}</Text>
      </div>
      <Tabs defaultValue="inbox" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="inbox">{t("tabs.inbox")}</Tabs.Tab>
          <Tabs.Tab value="outbox">{t("tabs.outbox")}</Tabs.Tab>
          {canSend && <Tabs.Tab value="compose">{t("tabs.compose")}</Tabs.Tab>}
          {canGroups && <Tabs.Tab value="groups">{t("tabs.groups")}</Tabs.Tab>}
        </Tabs.List>
        <Tabs.Panel value="inbox" pt="md">
          <InboxTab />
        </Tabs.Panel>
        <Tabs.Panel value="outbox" pt="md">
          <OutboxTab />
        </Tabs.Panel>
        {canSend && (
          <Tabs.Panel value="compose" pt="md">
            <ComposeTab />
          </Tabs.Panel>
        )}
        {canGroups && (
          <Tabs.Panel value="groups" pt="md">
            <GroupsTab />
          </Tabs.Panel>
        )}
      </Tabs>
    </Stack>
  );
}
