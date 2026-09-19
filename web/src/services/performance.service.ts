/**
 * Performance module (Faz 5 çekirdeği): mirrors `Sense.Modules.Performance` REST API (`api/v1/performance`).
 * Review cycles (`/performance/cycles`), goal sub-flow (`/performance/cycles/{id}/participants/{id}/goals`),
 * goal categories (`/performance/goal-categories`), self-service (`/performance/my-assignments`, `/performance/my-goals`),
 * and the live PDF report (`/performance/cycles/{id}/participants/{id}/report`, fetched as a blob).
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import { parseContentDispositionFileName, saveDownloadedFile, type DownloadedFile } from "@/services/data-transfer.service";

const BASE = "/v1/performance";

export type ReviewKind = "competency" | "competencyAndGoal";
export type EvaluatorRelation = "self" | "manager" | "subordinate" | "peer";
export type CommentMode = "optional" | "required" | "disabled";
export type RatingScale = "zeroToHundred" | "fivePointAgreement";
export type EvaluatorLabelMode = "name" | "categoryOnly";
export type GoalEntryBy = "employee" | "firstManager";
export type ReviewCycleStatus = "draft" | "published" | "completed";
export type ReviewCompletionMode = "natural" | "forced";
export type ReviewParticipantStatus = "pending" | "inProgress" | "completed";
export type ReviewAssignmentStatus = "pending" | "inProgress" | "completed" | "skipped";
export type GoalStatus = "draft" | "approved" | "resultEntered" | "resultApproved";
export type ReviewReportKind = "detailed" | "summary";

export interface RelationConfig {
  relation: EvaluatorRelation;
  isEnabled: boolean;
  weight: number | null;
  minRequired: number | null;
}

export interface QuestionInput {
  order: number;
  text: string;
  weight: number | null;
  commentMode: CommentMode | null;
}

export interface CompetencyInput {
  name: string;
  weight: number | null;
  questions: QuestionInput[];
}

export interface QuestionDto {
  id: string;
  order: number;
  text: string;
  weight: number | null;
  commentMode: CommentMode | null;
}

export interface CompetencyDto {
  id: string;
  name: string;
  weight: number | null;
  questions: QuestionDto[];
}

export interface ReviewResponseDto {
  questionId: string;
  score: number;
  comment: string | null;
}

export interface ReviewAssignmentDto {
  id: string;
  relation: EvaluatorRelation;
  evaluatorEmployeeId: string;
  status: ReviewAssignmentStatus;
  completedAtUtc: string | null;
  responses: ReviewResponseDto[];
}

export interface GoalDto {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  targetValue: number;
  actualValue: number | null;
  points: number | null;
  resultComment: string | null;
  weight: number | null;
  status: GoalStatus;
}

export interface ReviewParticipantDto {
  id: string;
  employeeId: string;
  includeGoals: boolean;
  status: ReviewParticipantStatus;
  competencyProgress: number;
  competencyScore: number | null;
  goalProgress: number;
  goalScore: number | null;
  finalScore: number | null;
  completedAtUtc: string | null;
  assignments: ReviewAssignmentDto[];
  goals: GoalDto[];
}

export interface ReviewCycleDto {
  id: string;
  name: string;
  periodStart: string;
  periodEnd: string;
  kind: ReviewKind;
  ratingScale: RatingScale;
  defaultCommentMode: CommentMode;
  evaluatorLabelMode: EvaluatorLabelMode;
  reportShowPhoto: boolean;
  employeeCanViewResults: boolean;
  employeeCanDownloadPdf: boolean;
  goalEntryBy: GoalEntryBy;
  competencyWeight: number | null;
  status: ReviewCycleStatus;
  publishedAtUtc: string | null;
  completedAtUtc: string | null;
  completedBy: ReviewCompletionMode | null;
  relationConfigs: RelationConfig[];
  competencies: CompetencyDto[];
  participants: ReviewParticipantDto[];
}

export interface ReviewCycleSummaryDto {
  id: string;
  name: string;
  periodStart: string;
  periodEnd: string;
  status: ReviewCycleStatus;
  participantCount: number;
  completedParticipantCount: number;
}

export interface MyAssignmentDto {
  reviewCycleId: string;
  reviewCycleName: string;
  periodStart: string;
  periodEnd: string;
  assignmentId: string;
  relation: EvaluatorRelation;
  participantEmployeeId: string;
  status: ReviewAssignmentStatus;
}

export interface MyGoalDto {
  participantId: string;
  goalId: string;
  name: string;
  description: string | null;
  targetValue: number;
  actualValue: number | null;
  points: number | null;
  status: GoalStatus;
}

export interface MyGoalCycleDto {
  reviewCycleId: string;
  reviewCycleName: string;
  periodStart: string;
  periodEnd: string;
  goals: MyGoalDto[];
}

export interface MyReviewResultDto {
  reviewCycleId: string;
  participantId: string;
  reviewCycleName: string;
  periodStart: string;
  periodEnd: string;
  kind: ReviewKind;
  status: ReviewParticipantStatus;
  competencyProgress: number;
  goalProgress: number;
  canViewResults: boolean;
  canDownloadPdf: boolean;
  finalScore: number | null;
}

export interface GoalCategoryDto {
  id: string;
  name: string;
}

export interface CreateReviewCycleInput {
  name: string;
  periodStart: string;
  periodEnd: string;
  kind: ReviewKind;
  ratingScale: RatingScale;
  defaultCommentMode: CommentMode;
  evaluatorLabelMode: EvaluatorLabelMode;
  reportShowPhoto: boolean;
  employeeCanViewResults: boolean;
  employeeCanDownloadPdf: boolean;
  goalEntryBy: GoalEntryBy;
  competencyWeight: number | null;
  relationConfigs: RelationConfig[];
  competencies: CompetencyInput[];
}

export type UpdateReviewCycleDraftInput = CreateReviewCycleInput;

export interface ParticipantEvaluatorsInput {
  relation: EvaluatorRelation;
  evaluatorEmployeeIds: string[];
}

export interface AddParticipantInput {
  employeeId: string;
  includeGoals: boolean;
  evaluators: ParticipantEvaluatorsInput[];
}

export interface ReviewAnswerInput {
  questionId: string;
  score: number;
  comment: string | null;
}

export interface GoalScaleRangeInput {
  min: number;
  max: number;
  points: number;
}

export interface AddGoalInput {
  participantEmployeeId: string;
  categoryId: string;
  name: string;
  description: string | null;
  targetValue: number;
  scale: GoalScaleRangeInput[];
  weight: number | null;
}

export async function listReviewCycles(): Promise<ReviewCycleSummaryDto[]> {
  const { data } = await apiClient.get<ReviewCycleSummaryDto[]>(`${BASE}/cycles`);
  return data;
}

export async function getReviewCycle(id: string): Promise<ReviewCycleDto> {
  const { data } = await apiClient.get<ReviewCycleDto>(`${BASE}/cycles/${id}`);
  return data;
}

export async function createReviewCycle(input: CreateReviewCycleInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/cycles`, input);
  return data;
}

export async function updateReviewCycleDraft(id: string, input: UpdateReviewCycleDraftInput): Promise<void> {
  await apiClient.put(`${BASE}/cycles/${id}`, input);
}

export async function addParticipants(cycleId: string, participants: AddParticipantInput[]): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${cycleId}/participants`, { participants });
}

export async function publishReviewCycle(id: string): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${id}/publish`);
}

/** Returns the number of participants force-completed. */
export async function forceCompleteReviewCycle(id: string): Promise<number> {
  const { data } = await apiClient.post<number>(`${BASE}/cycles/${id}/force-complete`);
  return data;
}

export async function downloadReviewReport(cycleId: string, participantId: string, participantEmployeeId: string, kind: ReviewReportKind): Promise<DownloadedFile> {
  try {
    const response = await apiClient.get<Blob>(`${BASE}/cycles/${cycleId}/participants/${participantId}/report`, {
      params: { participantEmployeeId, kind },
      responseType: "blob",
    });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? "rapor.pdf",
    };
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.data instanceof Blob) {
      try {
        const text = await error.response.data.text();
        error.response.data = text ? JSON.parse(text) : undefined;
      } catch {
        error.response.data = undefined;
      }
    }
    throw error;
  }
}

export { saveDownloadedFile };

// ---------------------------------------------------------------- Değerlendirici tarafı (performance.form.fill)

export interface AssignmentFormQuestion {
  id: string;
  order: number;
  text: string;
  commentModeUsed: CommentMode;
}

export interface AssignmentFormCompetency {
  name: string;
  questions: AssignmentFormQuestion[];
}

export interface AssignmentFormDto {
  reviewCycleId: string;
  reviewCycleName: string;
  assignmentId: string;
  relation: EvaluatorRelation;
  status: ReviewAssignmentStatus;
  ratingScale: RatingScale;
  competencies: AssignmentFormCompetency[];
  existingResponses: ReviewResponseDto[];
}

export async function getAssignmentForm(cycleId: string, assignmentId: string): Promise<AssignmentFormDto> {
  const { data } = await apiClient.get<AssignmentFormDto>(`${BASE}/cycles/${cycleId}/assignments/${assignmentId}/form`);
  return data;
}

export async function submitReviewResponse(cycleId: string, assignmentId: string, answers: ReviewAnswerInput[]): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${cycleId}/assignments/${assignmentId}/submit`, { answers });
}

// ---------------------------------------------------------------- Hedef akışı (performance.goals.*)

export async function addGoal(cycleId: string, participantId: string, input: AddGoalInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/cycles/${cycleId}/participants/${participantId}/goals`, input);
  return data;
}

export async function approveGoals(cycleId: string, participantId: string, participantEmployeeId: string): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${cycleId}/participants/${participantId}/goals/approve`, { participantEmployeeId });
}

export async function enterGoalResult(
  cycleId: string,
  participantId: string,
  goalId: string,
  participantEmployeeId: string,
  actualValue: number,
  resultComment: string | null
): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${cycleId}/participants/${participantId}/goals/${goalId}/result`, {
    participantEmployeeId,
    actualValue,
    resultComment,
  });
}

export async function approveGoalResults(cycleId: string, participantId: string, participantEmployeeId: string): Promise<void> {
  await apiClient.post(`${BASE}/cycles/${cycleId}/participants/${participantId}/goals/approve-results`, { participantEmployeeId });
}

// ---------------------------------------------------------------- Hedef kategorileri (performance.goal_category.manage)

export async function listGoalCategories(): Promise<GoalCategoryDto[]> {
  const { data } = await apiClient.get<GoalCategoryDto[]>(`${BASE}/goal-categories`);
  return data;
}

export async function createGoalCategory(name: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/goal-categories`, { name });
  return data;
}

// ---------------------------------------------------------------- Self-servis (performance.my.assignments)

export async function getMyAssignments(): Promise<MyAssignmentDto[]> {
  const { data } = await apiClient.get<MyAssignmentDto[]>(`${BASE}/my-assignments`);
  return data;
}

export async function getMyGoals(): Promise<MyGoalCycleDto[]> {
  const { data } = await apiClient.get<MyGoalCycleDto[]>(`${BASE}/my-goals`);
  return data;
}

export async function getMyResults(): Promise<MyReviewResultDto[]> {
  const { data } = await apiClient.get<MyReviewResultDto[]>(`${BASE}/my-results`);
  return data;
}
