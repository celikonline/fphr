import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Button,
  Checkbox,
  Group,
  MultiSelect,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
  Textarea,
} from "@mantine/core";
import SelectionField from "@/components/messages/selection-field";
import { isSelectionEmpty } from "@/components/messages/selection-summary";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  HR_MESSAGES_KEY,
  HR_MESSAGE_BODY_MAX,
  HR_MESSAGE_SUBJECT_MAX,
  MESSAGE_GROUPS_KEY,
  listMessageGroups,
  sendHrMessage,
  type HrMessageChannel,
  type HrMessageImportance,
} from "@/services/hr-messages.service";
import type { EmployeeSelection } from "@/types";
import { usePermission } from "@/components/permission-guard";

export default function ComposeTab() {
  const { t } = useTranslation("messages");
  const queryClient = useQueryClient();
  const canGroups = usePermission("notifications.messagegroups.manage");
  const [selection, setSelection] = useState<EmployeeSelection | null>(null);
  const [groupIds, setGroupIds] = useState<string[]>([]);
  const [channels, setChannels] = useState<HrMessageChannel[]>(["email", "in_app"]);
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [importance, setImportance] = useState<HrMessageImportance>("normal");

  const groups = useQuery({
    queryKey: MESSAGE_GROUPS_KEY,
    queryFn: listMessageGroups,
    enabled: canGroups,
  });

  const send = useMutation({
    mutationFn: sendHrMessage,
    onSuccess: (result) => {
      toast({ title: t("compose.sent", { count: result.recipientCount }) });
      setSelection(null);
      setGroupIds([]);
      setSubject("");
      setBody("");
      setImportance("normal");
      void queryClient.invalidateQueries({ queryKey: HR_MESSAGES_KEY });
    },
    onError: (error) =>
      toast({ title: getApiErrorMessage(error, t("compose.sendError")), variant: "destructive" }),
  });

  const hasRecipients = !isSelectionEmpty(selection) || groupIds.length > 0;
  const canSend =
    hasRecipients &&
    channels.length > 0 &&
    subject.trim().length > 0 &&
    body.trim().length > 0 &&
    body.length <= HR_MESSAGE_BODY_MAX &&
    !send.isPending;

  const toggleChannel = (channel: HrMessageChannel, checked: boolean) =>
    setChannels((prev) => (checked ? [...prev, channel] : prev.filter((c) => c !== channel)));

  function submit() {
    if (!canSend) return;
    send.mutate({
      subject: subject.trim(),
      body,
      importance,
      channels,
      selection: isSelectionEmpty(selection) ? null : selection,
      groupIds: groupIds.length > 0 ? groupIds : undefined,
    });
  }

  return (
    <Stack gap="md" maw={720}>
      <Stack gap="xs">
        <Text fw={600}>{t("compose.recipients")}</Text>
        <SelectionField value={selection} onChange={setSelection} />
        {canGroups && (
          <MultiSelect
            label={t("compose.groups")}
            placeholder={t("compose.groupsPlaceholder")}
            nothingFoundMessage={t("compose.noGroups")}
            data={(groups.data ?? []).map((g) => ({ value: g.id, label: g.name }))}
            value={groupIds}
            onChange={setGroupIds}
            searchable
          />
        )}
        {!hasRecipients && (
          <Text size="sm" c="dimmed">
            {t("compose.noRecipients")}
          </Text>
        )}
      </Stack>

      <Stack gap="xs">
        <Text fw={600}>{t("compose.channels")}</Text>
        <Group>
          <Checkbox
            label={t("channels.email")}
            checked={channels.includes("email")}
            onChange={(e) => toggleChannel("email", e.currentTarget.checked)}
          />
          <Checkbox
            label={t("channels.in_app")}
            checked={channels.includes("in_app")}
            onChange={(e) => toggleChannel("in_app", e.currentTarget.checked)}
          />
          <Checkbox
            label={`${t("channels.sms")} (${t("channels.smsSoon")})`}
            checked={false}
            disabled
            title={t("channels.smsHint")}
            onChange={() => {}}
          />
        </Group>
        <Text size="xs" c="dimmed">
          {t("channels.smsHint")} {t("compose.noAccount")}
        </Text>
        {channels.length === 0 && <Alert color="orange">{t("compose.noChannel")}</Alert>}
      </Stack>

      <TextInput
        label={t("compose.subject")}
        value={subject}
        maxLength={HR_MESSAGE_SUBJECT_MAX}
        onChange={(e) => setSubject(e.currentTarget.value)}
        required
      />
      <Textarea
        label={t("compose.body")}
        value={body}
        minRows={6}
        autosize
        maxLength={HR_MESSAGE_BODY_MAX}
        onChange={(e) => setBody(e.currentTarget.value)}
        description={
          <span data-testid="body-counter">
            {t("compose.counter", { count: body.length, max: HR_MESSAGE_BODY_MAX })}
          </span>
        }
        required
      />
      <Stack gap={4}>
        <Text size="sm" fw={500}>
          {t("compose.importance")}
        </Text>
        <SegmentedControl
          value={importance}
          onChange={(v) => setImportance(v as HrMessageImportance)}
          data={[
            { value: "normal", label: t("importance.normal") },
            { value: "high", label: t("importance.high") },
          ]}
        />
      </Stack>
      <Group>
        <Button onClick={submit} disabled={!canSend} loading={send.isPending}>
          {t("compose.send")}
        </Button>
      </Group>
    </Stack>
  );
}
