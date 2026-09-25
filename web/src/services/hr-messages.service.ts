/** İK mesajları (notifications.messages.send/view, notifications.messagegroups.manage). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/notifications/messages";

export interface HrMessage {
  id: string;
  subject: string;
  body: string;
  sentAt: string;
  recipientCount: number;
}

export interface SendHrMessageInput {
  subject: string;
  body: string;
  employeeIds?: string[];
  groupIds?: string[];
}

export async function listHrMessages(): Promise<HrMessage[]> {
  const { data } = await apiClient.get<HrMessage[]>(BASE);
  return data;
}

export async function sendHrMessage(dto: SendHrMessageInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(BASE, dto);
  return data.id;
}
