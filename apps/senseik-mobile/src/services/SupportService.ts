import { apiClient } from "@/lib/apiClient";

/**
 * "Yardım Merkezi + Desteğe bağlan" (11.3.13, roadmap #69). Mirrors `web/src/services/support.service.ts` —
 * same backend (`SupportController.cs`), makale okuma/arama izin gerektirmez, talep açma her role
 * "announcements.my.support_tickets" self-service jokeriyle otomatik ulaşır.
 */
const SUPPORT_BASE = "/api/v1/support";

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

/** Chip order of the "Desteğe bağlan" categories; labels are `support:categories.<value>`. */
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

export type SupportTicketStatus = "open" | "answered" | "closed";

/** Every known ticket status; labels are `support:status.<value>`. */
export const SUPPORT_TICKET_STATUSES: readonly SupportTicketStatus[] = [
  "open",
  "answered",
  "closed",
];

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
}

export interface CreateSupportTicketInput {
  subject: string;
  body: string;
  category: SupportTicketCategory;
  sourceRoute?: string;
}

export class SupportService {
  async searchArticles(q?: string): Promise<HelpArticleSummary[]> {
    const response = await apiClient.get<HelpArticleSummary[]>(`${SUPPORT_BASE}/articles`, {
      params: q ? { q } : undefined,
    });
    return response.data;
  }

  async getArticle(slug: string): Promise<HelpArticleDetail> {
    const response = await apiClient.get<HelpArticleDetail>(`${SUPPORT_BASE}/articles/${slug}`);
    return response.data;
  }

  async createTicket(input: CreateSupportTicketInput): Promise<string> {
    const response = await apiClient.post<string>(`${SUPPORT_BASE}/tickets`, {
      ...input,
      sourceRoute: input.sourceRoute ?? "mobile",
    });
    return response.data;
  }

  async listMyTickets(): Promise<SupportTicket[]> {
    const response = await apiClient.get<SupportTicket[]>(`${SUPPORT_BASE}/tickets/mine`);
    return response.data;
  }
}

export const supportService = new SupportService();
