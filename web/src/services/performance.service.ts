/**
 * Performance module: mirrors `GoalsController.cs` / `ReviewCyclesController.cs`
 * (`api/v1/performance/goals`, `api/v1/performance/review-cycles`). Sözleşme belgesi:
 * `docs/implementation/faz8-performance-api-sozlesmesi.md` (BE ikizim yazıyor - kod bu belgeyle
 * uyuşmuyorsa belge doğrudur, bu dosyayı ona göre güncelle).
 * Reads: own goals need performance.my.goals, team goals performance.team.goals, cancel needs
 * performance.goals.manage. Own review cycles need performance.my.reviews. Missing fields are
 * omitted by the API (`WhenWritingNull`) - treat them as null/undefined here.
 */
import { apiClient } from "@senseik/api-client";

const PERFORMANCE_BASE = "/v1/performance";

/** Mirrors `GoalStatus` (camelCase enum serialization). */
export type GoalStatus = "open" | "inProgress" | "completed" | "cancelled" | "archived";

/** Mirrors `ReviewCycleStatus`. */
export type ReviewCycleStatus = "draft" | "active" | "closed";

/**
 * `GoalDto`. `employeeName` is filled by the API for `GET goals/team` (resolved via `IEmployeeLookup`);
 * it is null/missing on the `me` endpoint and when the lookup fails.
 */
export interface Goal {
  id: string;
  employeeId: string;
  title: string;
  description?: string | null;
  /** "yyyy-MM-dd" (DateOnly). */
  targetDate: string;
  /** 0-100. */
  progress: number;
  status: GoalStatus;
  reviewCycleId?: string | null;
  employeeName?: string | null;
}

/** `ReviewCycleDto`. */
export interface ReviewCycle {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  status: ReviewCycleStatus;
}

/** Mirrors `ReviewFormStatus`. */
export type ReviewFormStatus = "draft" | "published";

/** `ReviewFormQuestionDto`. */
export interface ReviewFormQuestion {
  id: string;
  text: string;
  /** int, 0-100; sorular toplamı 100 olmalı. */
  weight: number;
  order: number;
}

/** `ReviewFormDto`. */
export interface ReviewForm {
  id: string;
  reviewCycleId: string;
  title: string;
  status: ReviewFormStatus;
  /** Her zaman `order` alanına göre sıralı döner. */
  questions: ReviewFormQuestion[];
}

/** `CreateReviewCycleRequest`. */
export interface CreateReviewCycleInput {
  name: string;
  startDate: string;
  endDate: string;
}

/** `CreateReviewFormRequest`. */
export interface CreateReviewFormInput {
  reviewCycleId: string;
  title: string;
  questions: { text: string; weight: number }[];
}

/** `CreateMyGoalRequest`. */
export interface CreateMyGoalInput {
  title: string;
  description?: string | null;
  targetDate: string;
  reviewCycleId?: string | null;
}

/** `CreateGoalRequest` (İK/yönetici - başkasına hedef ataması). */
export interface CreateGoalInput {
  employeeId: string;
  title: string;
  description?: string | null;
  targetDate: string;
  reviewCycleId?: string | null;
}

export async function getMyGoals(): Promise<Goal[]> {
  const { data } = await apiClient.get<Goal[]>(`${PERFORMANCE_BASE}/goals/me`);
  return data;
}

export async function createMyGoal(input: CreateMyGoalInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${PERFORMANCE_BASE}/goals/me`, input);
  return data;
}

export async function updateMyGoalProgress(id: string, progress: number): Promise<void> {
  await apiClient.post(`${PERFORMANCE_BASE}/goals/${id}/progress/me`, { progress });
}

/** performance.team.goals (yöneticinin ekip hedefleri, salt okunur - iptal dışında). */
export async function getTeamGoals(): Promise<Goal[]> {
  const { data } = await apiClient.get<Goal[]>(`${PERFORMANCE_BASE}/goals/team`);
  return data;
}

/** performance.goals.manage (İK/yönetici bir çalışana hedef atar). `goals` ucu diğerlerinin aksine `{id}` sarmalamaz, ham Guid döner (bkz. `createMyGoal`). */
export async function createGoal(input: CreateGoalInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${PERFORMANCE_BASE}/goals`, input);
  return data;
}

/** performance.goals.manage. */
export async function cancelGoal(id: string): Promise<void> {
  await apiClient.post(`${PERFORMANCE_BASE}/goals/${id}/cancel`);
}

/** performance.my.reviews ("Hedef ekle" formundaki dönem seçimi). */
export async function getMyReviewCycles(): Promise<ReviewCycle[]> {
  const { data } = await apiClient.get<ReviewCycle[]>(`${PERFORMANCE_BASE}/review-cycles/me`);
  return data;
}

/** performance.reviews.view (İK/yönetici dönem yönetimi - tüm dönemler, Draft dahil). */
export async function listReviewCycles(status?: ReviewCycleStatus): Promise<ReviewCycle[]> {
  const { data } = await apiClient.get<ReviewCycle[]>(`${PERFORMANCE_BASE}/review-cycles`, {
    params: status ? { status } : undefined,
  });
  return data;
}

/** performance.reviews.manage. Yeni dönem her zaman Draft durumunda oluşur. */
export async function createReviewCycle(input: CreateReviewCycleInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${PERFORMANCE_BASE}/review-cycles`, input);
  return data.id;
}

/** performance.reviews.manage. Draft -> Active. */
export async function activateReviewCycle(id: string): Promise<void> {
  await apiClient.post(`${PERFORMANCE_BASE}/review-cycles/${id}/activate`);
}

/** performance.reviews.approve (.manage değil - kapatma daha yüksek yetki ister). Active -> Closed, terminal. */
export async function closeReviewCycle(id: string): Promise<void> {
  await apiClient.post(`${PERFORMANCE_BASE}/review-cycles/${id}/close`);
}

/** performance.reviews.view. Sorular `order` alanına göre sıralı döner. */
export async function listReviewForms(reviewCycleId: string): Promise<ReviewForm[]> {
  const { data } = await apiClient.get<ReviewForm[]>(`${PERFORMANCE_BASE}/review-forms`, {
    params: { reviewCycleId },
  });
  return data;
}

/** performance.reviews.manage. Yeni form her zaman Draft, sorularıyla birlikte tek istekte oluşur. */
export async function createReviewForm(input: CreateReviewFormInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${PERFORMANCE_BASE}/review-forms`, input);
  return data.id;
}

/** performance.reviews.manage. Draft -> Published, terminal (yeni soru için yeni form gerekir). */
export async function publishReviewForm(id: string): Promise<void> {
  await apiClient.post(`${PERFORMANCE_BASE}/review-forms/${id}/publish`);
}
