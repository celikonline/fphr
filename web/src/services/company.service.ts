/**
 * Company Profile Service
 *
 * Screen 1 (Item 20): view/edit the tenant's company profile + logo upload.
 *
 * TODO(item-20-backend): There is no CompaniesController / rich tenant-profile endpoint yet.
 * The only real tenant data today is the thin `Tenant` row (id/slug/name/taxNumber/locale/timeZone)
 * exposed via `/v1/identity/tenants` on the auth side - it has no address, operating hours, working
 * days, or logo. Until a backend endpoint exists (e.g. `GET/PUT /v1/identity/tenants/{id}/profile`),
 * this service persists the profile to localStorage so the screen is fully usable end-to-end.
 * Swap the two functions below for real `apiClient` calls once that endpoint exists - the shape of
 * `CompanyProfile` was designed to match the task's field list so no page changes should be needed.
 *
 * Logo upload IS wired to the real Documents module (Item 15), which does exist:
 *   POST /v1/documents/initiate-upload -> presigned URL
 *   PUT  <presignedUrl>                -> upload bytes directly to storage
 *   POST /v1/documents/{id}/confirm-upload -> finalize
 * Known gap: `InitiateUploadResponse` does not return the new Document's id (only presignedUrl/
 * requestId/expiresAt), so the confirm step cannot be reliably called yet - see the inline note in
 * `uploadCompanyLogo`. That's a Documents module contract gap (Item 15), not something to patch here.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import { mockDelay, readMock, writeMock } from "@/services/mock/local-store";

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

const MOCK_KEY = "senseik.mock.company-profile";

const DEFAULT_PROFILE: CompanyProfile = {
  id: "current-tenant",
  name: "Algosense Demo A.Ş.",
  registrationNumber: "0123456789",
  taxNumber: "1234567890",
  address: "Maslak Mahallesi, Büyükdere Caddesi No:1, Sarıyer",
  city: "İstanbul",
  country: "Türkiye",
  operatingHours: { start: "09:00", end: "18:00" },
  workingDays: ["mon", "tue", "wed", "thu", "fri"],
  updatedAt: new Date().toISOString(),
};

export async function getCompanyProfile(): Promise<CompanyProfile> {
  await mockDelay();
  return readMock(MOCK_KEY, DEFAULT_PROFILE);
}

export async function updateCompanyProfile(patch: Partial<CompanyProfile>): Promise<CompanyProfile> {
  await mockDelay();
  const current = readMock(MOCK_KEY, DEFAULT_PROFILE);
  const updated: CompanyProfile = { ...current, ...patch, updatedAt: new Date().toISOString() };
  return writeMock(MOCK_KEY, updated);
}

export interface UploadLogoResult {
  documentId?: string;
  logoUrl: string;
}

/**
 * Uploads a new company logo through the real Documents presigned-URL flow and stores the
 * reference on the mock company profile. Falls back to a local object URL for instant preview
 * (and as the only available preview while the Documents confirm-step gap above is unresolved).
 */
export async function uploadCompanyLogo(file: File): Promise<UploadLogoResult> {
  const objectUrl = URL.createObjectURL(file);

  try {
    const { data: initiated } = await apiClient.post<{
      presignedUrl: string;
      requestId: string;
      expiresAt: string;
    }>("/v1/documents/initiate-upload", {
      fileName: file.name,
      contentType: file.type || "application/octet-stream",
      fileSizeBytes: file.size,
      bucketName: "logos",
    });

    // Upload bytes directly to storage via the presigned URL (not our API - no auth header, plain axios).
    const uploadResponse = await axios.put(initiated.presignedUrl, file, {
      headers: { "Content-Type": file.type || "application/octet-stream" },
    });
    const etag = (uploadResponse.headers["etag"] as string | undefined) ?? initiated.requestId;

    // NOTE: `initiated.requestId` is the storage request id, not the Document's id, so this
    // confirm call is best-effort until the Documents module returns a real documentId
    // (see the file-level TODO). We swallow a failure here and keep the local preview.
    try {
      await apiClient.post(`/v1/documents/${initiated.requestId}/confirm-upload`, {
        eTag: etag,
        actualFileSizeBytes: file.size,
      });
    } catch {
      // Expected until the backend contract gap is fixed - logo still "uploads" from the user's
      // point of view because we keep the object URL below.
    }

    await updateCompanyProfile({ logoUrl: objectUrl });
    return { logoUrl: objectUrl };
  } catch (error) {
    console.error("Logo upload failed, falling back to local preview only:", error);
    await updateCompanyProfile({ logoUrl: objectUrl });
    return { logoUrl: objectUrl };
  }
}
