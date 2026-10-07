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
} from "@mantine/core";
import MessageRichEditor from "@/components/messages/message-rich-editor";
import SelectionField from "@/components/messages/selection-field";
import { isSelectionEmpty } from "@/components/messages/selection-summary";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  HR_MESSAGES_KEY,
  HR_MESSAGE_BODY_MAX,
  HR_MESSAGE_BODY_STORAGE_MAX,
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
  const [bodyTextLength, setBodyTextLength] = useState(0);
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
      setBodyTextLength(0);
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
    bodyTextLength > 0 &&
    bodyTextLength <= HR_MESSAGE_BODY_MAX &&
    body.length <= HR_MESSAGE_BODY_STORAGE_MAX &&
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
      ...(groupIds.length > 0 ? { groupIds } : {}),
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
      <Stack gap={4}>
        <Text component="label" fw={500} size="sm">
          {t("compose.body")} <span aria-hidden="true">*</span>
        </Text>
        <MessageRichEditor
          content={body}
          maxTextLength={HR_MESSAGE_BODY_MAX}
          onChange={(html, text) => {
            setBody(html);
            setBodyTextLength(text.length);
          }}
        />
        <Text size="xs" c="dimmed" data-testid="body-counter">
          {t("compose.counter", { count: bodyTextLength, max: HR_MESSAGE_BODY_MAX })}
        </Text>
      </Stack>
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
