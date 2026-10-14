/**
 * Yardım Merkezi + Desteğe bağlan (11.3.13, roadmap #69). Makale arama/okuma her kimliği doğrulanmış kullanıcıya
 * açık; talep açma/kendi taleplerini görme "announcements.my.support_tickets" ile her role otomatik ulaşır.
 */
import { apiClient } from "@senseik/api-client";

const SUPPORT_BASE = "/v1/support";

/** Mirrors the domain `SupportTicketCategory` enum (JsonStringEnumConverter CamelCase). */
export type SupportTicketCategory =
  | "general"
  | "dataTransfer"
  | "leaveAndOvertime"
  | "approvals"
  | "accessAndPermissions"
  | "payroll"
  | "billing"
  | "other";

export const SUPPORT_TICKET_CATEGORIES: SupportTicketCategory[] = [
  "general",
  "dataTransfer",
  "leaveAndOvertime",
  "approvals",
  "accessAndPermissions",
  "payroll",
  "billing",
  "other",
];

/** Mirrors the domain `SupportTicketStatus` enum. */
export type SupportTicketStatus = "open" | "answered" | "closed";

export type PortalTicketStatus =
  | "new"
  | "triaged"
  | "in_progress"
  | "waiting_requester"
  | "waiting_internal"
  | "resolved"
  | "closed"
  | "cancelled";

export interface SupportPortal {
  ticketId?: string;
  number?: string;
  status?: PortalTicketStatus;
  syncState: "pending" | "connected" | "unavailable";
  commentsTruncated?: boolean;
  comments: { id: string; body: string; createdAt: string; authorName?: string; source?: string }[];
}

export interface HelpArticleSummary {
  slug: string;
  category: string;
  title: string;
  relatedRoutes: string[];
}

export interface HelpArticleDetail {
  slug: string;
  category: string;
  title: string;
  body: string;
  media: string[];
}

export interface SupportTicket {
  id: string;
  requestedByUserId: string;
  subject: string;
  body: string;
  category: SupportTicketCategory;
  channel: string;
  status: SupportTicketStatus;
  sourceRoute: string | null;
  resolution: string | null;
  answeredAt: string | null;
  closedAt: string | null;
  createdAt: string;
  portal?: SupportPortal;
}

export async function searchHelpArticles(
  params: { q?: string; route?: string } = {}
): Promise<HelpArticleSummary[]> {
  const { data } = await apiClient.get<HelpArticleSummary[]>(`${SUPPORT_BASE}/articles`, {
    params,
  });
  return data;
}

export async function getHelpArticle(slug: string): Promise<HelpArticleDetail> {
  const { data } = await apiClient.get<HelpArticleDetail>(`${SUPPORT_BASE}/articles/${slug}`);
  return data;
}

export interface CreateSupportTicketInput {
  subject: string;
  body: string;
  category: SupportTicketCategory;
  sourceRoute?: string;
}

export async function createSupportTicket(input: CreateSupportTicketInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${SUPPORT_BASE}/tickets`, input);
  return data;
}

export async function listMySupportTickets(): Promise<SupportTicket[]> {
  const { data } = await apiClient.get<SupportTicket[]>(`${SUPPORT_BASE}/tickets/mine`);
  return data;
}

export async function getSupportTicket(ticketId: string): Promise<SupportTicket> {
  const { data } = await apiClient.get<SupportTicket>(
    `${SUPPORT_BASE}/tickets/${encodeURIComponent(ticketId)}`
  );
  return data;
}

export async function addSupportTicketComment(
  ticketId: string,
  body: string,
  idempotencyKey: string
): Promise<void> {
  await apiClient.post(
    `${SUPPORT_BASE}/tickets/${encodeURIComponent(ticketId)}/comments`,
    { body },
    {
      headers: { "Idempotency-Key": idempotencyKey },
    }
  );
}
