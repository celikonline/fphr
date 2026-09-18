/**
 * Company Profile Service
 *
 * Screen 1 (Item 20): view/edit the tenant's company profile + logo upload.
 *
 * Faz 2 (TB-10): wired to the real backend now - `GET/PUT /v1/organization/companies/profile` and
 * `PUT /v1/organization/companies/profile/logo` (Sense.Modules.Organization.Api.CompaniesController).
 * The `CompanyProfile` shape below is unchanged from the pre-existing mock (see structure.service.ts's
 * `Company` for the separate, narrower "Şirket ekle" DTO - deliberately not reused here, they serve
 * different screens/contracts).
 *
 * Logo upload is wired to the real Documents module (Item 15) via `uploadDocumentFile`
 * (documents.service.ts): initiate-upload -> PUT bytes directly to storage -> confirm-upload, then the
 * resulting documentId + a local preview URL are persisted on the profile via the dedicated logo endpoint.
 */
import { apiClient } from "@senseik/api-client";
import { uploadDocumentFile } from "@/services/documents.service";

const PROFILE_BASE = "/v1/organization/companies/profile";

export interface OperatingHours {
  start: string; // "09:00"
  end: string; // "18:00"
}

export type WeekDay = "mon" | "tue" | "wed" | "thu" | "fri" | "sat" | "sun";

export interface CompanyProfile {
  id: string;
  name: string;
  registrationNumber: string;
  taxNumber: string;
  address: string;
  city: string;
  country: string;
  operatingHours: OperatingHours;
  workingDays: WeekDay[];
  logoDocumentId?: string;
  logoUrl?: string;
  updatedAt: string;
}

interface CompanyProfileResponse {
  id: string;
  orgUnitId: string;
  name: string;
  registrationNumber?: string;
  taxNumber?: string;
  address?: string;
  city?: string;
  country?: string;
  operatingHoursStart?: string;
  operatingHoursEnd?: string;
  workingDays: WeekDay[];
  logoDocumentId?: string;
  logoUrl?: string;
  updatedAt: string;
}

function fromResponse(dto: CompanyProfileResponse): CompanyProfile {
  return {
    id: dto.id,
    name: dto.name,
    registrationNumber: dto.registrationNumber ?? "",
    taxNumber: dto.taxNumber ?? "",
    address: dto.address ?? "",
    city: dto.city ?? "",
    country: dto.country ?? "",
    operatingHours: { start: dto.operatingHoursStart ?? "09:00", end: dto.operatingHoursEnd ?? "18:00" },
    workingDays: dto.workingDays,
    logoDocumentId: dto.logoDocumentId,
    logoUrl: dto.logoUrl,
    updatedAt: dto.updatedAt,
  };
}

export async function getCompanyProfile(): Promise<CompanyProfile> {
  const { data } = await apiClient.get<CompanyProfileResponse>(PROFILE_BASE);
  return fromResponse(data);
}

/**
 * Saves the full "Şirket profili" form. Callers (the profile page) always submit the complete set of
 * fields - the backend command renames the company's org unit (`name`) and replaces the legal/registry
 * fields together, so a true partial PATCH isn't meaningful here.
 */
export async function updateCompanyProfile(patch: Partial<CompanyProfile>): Promise<CompanyProfile> {
  const { data } = await apiClient.put<CompanyProfileResponse>(PROFILE_BASE, {
    name: patch.name,
    registrationNumber: patch.registrationNumber,
    taxNumber: patch.taxNumber,
    address: patch.address,
    city: patch.city,
    country: patch.country,
    operatingHoursStart: patch.operatingHours?.start,
    operatingHoursEnd: patch.operatingHours?.end,
    workingDays: patch.workingDays,
  });
  return fromResponse(data);
}

export interface UploadLogoResult {
  documentId?: string;
  logoUrl: string;
}

/**
 * Uploads a new company logo through the real Documents presigned-URL flow, then persists the
 * documentId on the profile via the dedicated logo endpoint. Falls back to a local object URL for
 * instant preview if the upload itself fails.
 */
export async function uploadCompanyLogo(file: File): Promise<UploadLogoResult> {
  const objectUrl = URL.createObjectURL(file);

  try {
    const documentId = await uploadDocumentFile(file, "logos");
    await apiClient.put<CompanyProfileResponse>(`${PROFILE_BASE}/logo`, { logoDocumentId: documentId, logoUrl: objectUrl });
    return { documentId, logoUrl: objectUrl };
  } catch {
    // Log a short message only - the raw error can carry request URLs/stack traces.
    console.error("Logo upload failed, falling back to local preview only.");
    await apiClient.put<CompanyProfileResponse>(`${PROFILE_BASE}/logo`, { logoDocumentId: null, logoUrl: objectUrl });
    return { logoUrl: objectUrl };
  }
}
