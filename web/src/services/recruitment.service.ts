/**
 * Recruitment module (Faz 6 R0): mirrors `JobPostingsController`/`CandidatesController`/`JobApplicationsController.cs`
 * (`api/v1/recruitment/{postings,candidates,applications}`). R0 scope only: manual posting lifecycle, candidates
 * added manually, fixed pipeline stage set. Career site, requisitions, form builder etc. are later Recruitment
 * packages (see docs/analysis/kolayik-video/faz6-durum-takibi.md).
 */
import { apiClient } from "@senseik/api-client";

const RECRUITMENT_BASE = "/v1/recruitment";

export type JobPostingStatus = "draft" | "open" | "onHold" | "closed" | "archived";
export type JobPostingArchiveReason = "positionFilled" | "positionCancelled" | "other";
export const JOB_POSTING_ARCHIVE_REASONS: JobPostingArchiveReason[] = ["positionFilled", "positionCancelled", "other"];

/** Mirrors `RecruitmentEmploymentType` (same value set as Employee's EmploymentType, kept local until R1's IEmploymentTypeLookup). */
export type RecruitmentEmploymentType =
  | "fullTime"
  | "partTime"
  | "temporary"
  | "contract"
  | "freelance"
  | "intern"
  | "mobile"
  | "apprentice"
  | "seasonal"
  | "agencyWorker"
  | "subcontractor";
export const RECRUITMENT_EMPLOYMENT_TYPES: RecruitmentEmploymentType[] = [
  "fullTime",
  "partTime",
  "temporary",
  "contract",
  "freelance",
  "intern",
  "mobile",
  "apprentice",
  "seasonal",
  "agencyWorker",
  "subcontractor",
];

export interface JobPosting {
  id: string;
  title: string;
  orgUnitId: string;
  locationId: string | null;
  employmentType: RecruitmentEmploymentType;
  description: string | null;
  companyNotes: string | null;
  ownerUserId: string;
  hiringManagerUserId: string | null;
  requisitionId: string | null;
  status: JobPostingStatus;
  applySlug: string;
  closingDate: string | null;
  publishedAt: string | null;
  archivedAt: string | null;
  archiveReason: JobPostingArchiveReason | null;
  accessUserIds: string[];
}

export interface CreateJobPostingInput {
  title: string;
  orgUnitId: string;
  locationId?: string | null;
  employmentType: RecruitmentEmploymentType;
  description?: string | null;
  ownerUserId: string;
  hiringManagerUserId?: string | null;
  requisitionId?: string | null;
}

export interface UpdateJobPostingInput {
  title: string;
  orgUnitId: string;
  locationId?: string | null;
  employmentType: RecruitmentEmploymentType;
  description?: string | null;
  companyNotes?: string | null;
  hiringManagerUserId?: string | null;
  closingDate?: string | null;
}

export async function listJobPostings(status?: JobPostingStatus): Promise<JobPosting[]> {
  const { data } = await apiClient.get<JobPosting[]>(`${RECRUITMENT_BASE}/postings`, { params: { status } });
  return data;
}

export async function getJobPosting(id: string): Promise<JobPosting> {
  const { data } = await apiClient.get<JobPosting>(`${RECRUITMENT_BASE}/postings/${id}`);
  return data;
}

export async function createJobPosting(input: CreateJobPostingInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/postings`, input);
  return data;
}

export async function updateJobPosting(id: string, input: UpdateJobPostingInput): Promise<void> {
  await apiClient.put(`${RECRUITMENT_BASE}/postings/${id}`, input);
}

export async function publishJobPosting(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/postings/${id}/publish`);
}

export async function closeJobPosting(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/postings/${id}/close`);
}

export async function archiveJobPosting(id: string, reason: JobPostingArchiveReason): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/postings/${id}/archive`, { reason });
}

export async function getJobPostingAccess(id: string): Promise<string[]> {
  const { data } = await apiClient.get<string[]>(`${RECRUITMENT_BASE}/postings/${id}/access`);
  return data;
}

export async function setJobPostingAccess(id: string, userIds: string[]): Promise<void> {
  await apiClient.put(`${RECRUITMENT_BASE}/postings/${id}/access`, { userIds });
}

export type CandidateSource = "manual" | "careerSite" | "referral" | "other";

export interface Candidate {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  source: CandidateSource;
  tagIds: string[];
  /** KVKK imha uygulanmış mı (bkz. `anonymizeCandidate`/`anonymizeExpiredCandidates`). */
  isAnonymized: boolean;
}

export interface CandidateContactInput {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string | null;
}

export async function listCandidates(search?: string): Promise<Candidate[]> {
  const { data } = await apiClient.get<Candidate[]>(`${RECRUITMENT_BASE}/candidates`, { params: { search } });
  return data;
}

export async function getCandidate(id: string): Promise<Candidate> {
  const { data } = await apiClient.get<Candidate>(`${RECRUITMENT_BASE}/candidates/${id}`);
  return data;
}

export async function createCandidate(input: CandidateContactInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/candidates`, input);
  return data;
}

export async function updateCandidate(id: string, input: CandidateContactInput): Promise<void> {
  await apiClient.put(`${RECRUITMENT_BASE}/candidates/${id}`, input);
}

export async function setCandidateTags(id: string, tagIds: string[]): Promise<void> {
  await apiClient.put(`${RECRUITMENT_BASE}/candidates/${id}/tags`, { tagIds });
}

/** KVKK "unutulma hakkı": bu adayın kişisel verilerini geri alınamaz şekilde anonimleştirir. */
export async function anonymizeCandidate(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/candidates/${id}/anonymize`);
}

/** KVKK saklama süresi dolan tüm adayları anonimleştirir; anonimleştirilen aday sayısını döner. */
export async function anonymizeExpiredCandidates(): Promise<number> {
  const { data } = await apiClient.post<number>(`${RECRUITMENT_BASE}/candidates/anonymize-expired`);
  return data;
}

export type ApplicationStage = "applied" | "screening" | "interview" | "offer" | "hired" | "rejected";
export const APPLICATION_STAGES: ApplicationStage[] = ["applied", "screening", "interview", "offer", "hired", "rejected"];

export interface JobApplication {
  id: string;
  jobPostingId: string;
  candidateId: string;
  stage: ApplicationStage;
  appliedAt: string;
  archivedAt: string | null;
}

export async function listApplicationsByPosting(jobPostingId: string): Promise<JobApplication[]> {
  const { data } = await apiClient.get<JobApplication[]>(`${RECRUITMENT_BASE}/applications`, { params: { jobPostingId } });
  return data;
}

export async function listApplicationsByCandidate(candidateId: string): Promise<JobApplication[]> {
  const { data } = await apiClient.get<JobApplication[]>(`${RECRUITMENT_BASE}/candidates/${candidateId}/applications`);
  return data;
}

export async function createJobApplication(jobPostingId: string, candidateId: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/applications`, { jobPostingId, candidateId });
  return data;
}

export async function moveJobApplicationStage(id: string, stage: ApplicationStage): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/applications/${id}/stage`, { stage });
}

export type RequisitionReason = "newPosition" | "replacement";
export const REQUISITION_REASONS: RequisitionReason[] = ["newPosition", "replacement"];

export type RequisitionStatus = "draft" | "pendingApproval" | "approved" | "rejected" | "cancelled";

export interface JobRequisition {
  id: string;
  title: string;
  orgUnitId: string;
  employmentType: RecruitmentEmploymentType;
  headcountRequested: number;
  reason: RequisitionReason;
  justification: string | null;
  requesterEmployeeId: string;
  status: RequisitionStatus;
  approvalRequestId: string | null;
  decidedAt: string | null;
}

export interface CreateJobRequisitionInput {
  title: string;
  orgUnitId: string;
  employmentType: RecruitmentEmploymentType;
  headcountRequested: number;
  reason: RequisitionReason;
  justification?: string | null;
}

export async function listJobRequisitions(status?: RequisitionStatus): Promise<JobRequisition[]> {
  const { data } = await apiClient.get<JobRequisition[]>(`${RECRUITMENT_BASE}/requisitions`, { params: { status } });
  return data;
}

export async function getJobRequisition(id: string): Promise<JobRequisition> {
  const { data } = await apiClient.get<JobRequisition>(`${RECRUITMENT_BASE}/requisitions/${id}`);
  return data;
}

export async function createJobRequisition(input: CreateJobRequisitionInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/requisitions`, input);
  return data;
}

export async function submitJobRequisition(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/requisitions/${id}/submit`);
}

export async function cancelJobRequisition(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/requisitions/${id}/cancel`);
}

export interface Tag {
  id: string;
  name: string;
  isActive: boolean;
}

export async function listTags(activeOnly = false): Promise<Tag[]> {
  const { data } = await apiClient.get<Tag[]>(`${RECRUITMENT_BASE}/tags`, { params: { activeOnly } });
  return data;
}

export async function createTag(name: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/tags`, { name });
  return data;
}

export async function deactivateTag(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/tags/${id}/deactivate`);
}

export async function reactivateTag(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/tags/${id}/reactivate`);
}

export type FormTemplateType = "feedback" | "scorecard" | "outcome";
export const FORM_TEMPLATE_TYPES: FormTemplateType[] = ["feedback", "scorecard", "outcome"];

export type FormFieldType = "scoreCard" | "paragraph" | "singleLine" | "date" | "dropdown" | "checkbox";
export const FORM_FIELD_TYPES: FormFieldType[] = ["scoreCard", "paragraph", "singleLine", "date", "dropdown", "checkbox"];

export interface FormField {
  id: string;
  label: string;
  fieldType: FormFieldType;
  isRequired: boolean;
  isSensitive: boolean;
  sortOrder: number;
  options: string[];
  weight: number | null;
}

export interface FormFieldInput {
  id?: string | null;
  label: string;
  fieldType: FormFieldType;
  isRequired: boolean;
  isSensitive: boolean;
  options?: string[] | null;
  weight?: number | null;
}

export interface FormTemplate {
  id: string;
  name: string;
  templateType: FormTemplateType;
  isActive: boolean;
  fields: FormField[];
}

export async function listFormTemplates(activeOnly = false): Promise<FormTemplate[]> {
  const { data } = await apiClient.get<FormTemplate[]>(`${RECRUITMENT_BASE}/form-templates`, { params: { activeOnly } });
  return data;
}

export async function createFormTemplate(name: string, templateType: FormTemplateType, fields: FormFieldInput[]): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/form-templates`, { name, templateType, fields });
  return data;
}

export async function deactivateFormTemplate(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/form-templates/${id}/deactivate`);
}

export async function reactivateFormTemplate(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/form-templates/${id}/reactivate`);
}

export interface FieldAnswerInput {
  fieldId: string;
  value: string | null;
}

export interface FormResponse {
  id: string;
  templateId: string;
  jobApplicationId: string;
  submittedByEmployeeId: string;
  submittedAt: string;
  weightedScore: number | null;
  answers: { fieldId: string; value: string }[];
}

export async function listFormResponsesByApplication(jobApplicationId: string): Promise<FormResponse[]> {
  const { data } = await apiClient.get<FormResponse[]>(`${RECRUITMENT_BASE}/form-responses`, { params: { jobApplicationId } });
  return data;
}

export async function submitFormResponse(templateId: string, jobApplicationId: string, answers: FieldAnswerInput[]): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/form-responses`, { templateId, jobApplicationId, answers });
  return data;
}

export interface AutomationRule {
  id: string;
  fromStage: ApplicationStage;
  toStage: ApplicationStage;
  requiredFormTemplateId: string;
  isActive: boolean;
}

export async function listAutomationRules(activeOnly = false): Promise<AutomationRule[]> {
  const { data } = await apiClient.get<AutomationRule[]>(`${RECRUITMENT_BASE}/automation-rules`, { params: { activeOnly } });
  return data;
}

export async function createAutomationRule(fromStage: ApplicationStage, toStage: ApplicationStage, requiredFormTemplateId: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/automation-rules`, { fromStage, toStage, requiredFormTemplateId });
  return data;
}

export async function deactivateAutomationRule(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/automation-rules/${id}/deactivate`);
}

export async function reactivateAutomationRule(id: string): Promise<void> {
  await apiClient.post(`${RECRUITMENT_BASE}/automation-rules/${id}/reactivate`);
}

// ---------------------------------------------------------------- Public career site (anonymous, no-auth pages)
// Mirrors `PublicCareerController.cs` (`api/v1/recruitment/public/postings`). Tenant is resolved server-side from
// the posting's `ApplySlug` alone - never send/expect a tenant id here.

export interface PublicJobPosting {
  applySlug: string;
  title: string;
  employmentType: RecruitmentEmploymentType;
  description: string | null;
  closingDate: string | null;
  publishedAt: string | null;
  /** S-19 (`ILocationLookup`) ile çözülür; ilanın konumu yoksa veya anonim bağlamda ad bulunamazsa null. */
  location?: string | null;
}

export interface PublicJobPostingSummary {
  applySlug: string;
  title: string;
  /** S-19 (`ILocationLookup`) ile çözülür; ilanın konumu yoksa veya anonim bağlamda ad bulunamazsa null. */
  location?: string | null;
  employmentType: RecruitmentEmploymentType;
  publishedAt: string | null;
}

/**
 * `GET public/postings?tenant=` list item (S-10, tur 3 sözleşmesi). Ayrı tip: bu uç `location`/`employmentType`'ı
 * opsiyonel döner (`related` ucundaki `PublicJobPostingSummary`'den farklı sözleşme).
 */
export interface PublicJobPostingListItem {
  applySlug: string;
  title: string;
  location?: string | null;
  employmentType?: RecruitmentEmploymentType | null;
  publishedAt: string | null;
}

export interface SubmitCareerApplicationInput {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string | null;
  /** Aydınlatma metni versiyonu (bkz. `recruitment:career.privacyNoticeVersion`), Faz 1'in `IConsentRecorder`'ı gelene kadar başvuru ile birlikte taşınır. */
  privacyNoticeVersion: string;
  kvkkAccepted: boolean;
}

export async function getPublicJobPosting(applySlug: string): Promise<PublicJobPosting> {
  const { data } = await apiClient.get<PublicJobPosting>(`${RECRUITMENT_BASE}/public/postings/${applySlug}`);
  return data;
}

export async function listRelatedPublicJobPostings(applySlug: string): Promise<PublicJobPostingSummary[]> {
  const { data } = await apiClient.get<PublicJobPostingSummary[]>(`${RECRUITMENT_BASE}/public/postings/${applySlug}/related`);
  return data;
}

/** Bir kiracının yayında olan tüm ilanları (S-10). Kiracı slug'ı bulunamazsa BE boş dizi döner (404 değil). */
export async function listPublicJobPostingsByTenant(tenantSlug: string): Promise<PublicJobPostingListItem[]> {
  const { data } = await apiClient.get<PublicJobPostingListItem[]>(`${RECRUITMENT_BASE}/public/postings`, {
    params: { tenant: tenantSlug },
  });
  return data;
}

export async function submitCareerApplication(applySlug: string, input: SubmitCareerApplicationInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${RECRUITMENT_BASE}/public/postings/${applySlug}/apply`, input);
  return data;
}
