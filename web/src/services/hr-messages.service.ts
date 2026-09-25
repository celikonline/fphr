/**
 * İK mesajları (notifications.messages.send/view, notifications.messagegroups.manage).
 * Mirrors Sense.Modules.Notifications (HrMessagesController, MessageGroupsController). Nulls are omitted by the API.
 */
import { apiClient } from "@senseik/api-client";
import type { EmployeeSelection } from "@/types";

const MESSAGES_BASE = "/v1/notifications/messages";
const GROUPS_BASE = "/v1/notifications/message-groups";

export type HrMessageImportance = "normal" | "high";
/** "sms" is rejected by the server until a provider exists (SmsChannelUnavailable). */
export type HrMessageChannel = "email" | "in_app" | "sms";

export const HR_MESSAGE_BODY_MAX = 4096;
export const HR_MESSAGE_SUBJECT_MAX = 300;

export interface HrMessage {
  id: string;
  senderUserId: string;
  subject: string;
  body: string;
  importance: HrMessageImportance;
  channels: HrMessageChannel[];
  recipientCount: number;
  sentAt: string;
}

export interface HrMessagesPage {
  items: HrMessage[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface SendHrMessageInput {
  subject: string;
  body: string;
  importance: HrMessageImportance;
  channels: HrMessageChannel[];
  selection?: EmployeeSelection | null;
  groupIds?: string[];
}

export interface SendHrMessageResult {
  id: string;
  recipientCount: number;
}

export interface MessageGroup {
  id: string;
  name: string;
  selection?: EmployeeSelection | null;
}

export interface MessageGroupInput {
  name: string;
  selection: EmployeeSelection;
}

export const HR_MESSAGES_KEY = ["hr-messages"] as const;
export const MESSAGE_GROUPS_KEY = ["message-groups"] as const;

export async function listInbox(page = 1, pageSize = 20): Promise<HrMessagesPage> {
  const { data } = await apiClient.get<HrMessagesPage>(`${MESSAGES_BASE}/inbox`, {
    params: { page, pageSize },
  });
  return { ...data, items: data.items ?? [] };
}

export async function listOutbox(page = 1, pageSize = 20): Promise<HrMessagesPage> {
  const { data } = await apiClient.get<HrMessagesPage>(`${MESSAGES_BASE}/outbox`, {
    params: { page, pageSize },
  });
  return { ...data, items: data.items ?? [] };
}

export async function sendHrMessage(input: SendHrMessageInput): Promise<SendHrMessageResult> {
  const { data } = await apiClient.post<SendHrMessageResult>(MESSAGES_BASE, input);
  return data;
}

export async function listMessageGroups(): Promise<MessageGroup[]> {
  const { data } = await apiClient.get<MessageGroup[]>(GROUPS_BASE);
  return data ?? [];
}

export async function createMessageGroup(input: MessageGroupInput): Promise<string> {
  const { data } = await apiClient.post<string>(GROUPS_BASE, input);
  return data;
}

export async function updateMessageGroup(id: string, input: MessageGroupInput): Promise<void> {
  await apiClient.put(`${GROUPS_BASE}/${id}`, input);
}

export async function deleteMessageGroup(id: string): Promise<void> {
  await apiClient.delete(`${GROUPS_BASE}/${id}`);
}
