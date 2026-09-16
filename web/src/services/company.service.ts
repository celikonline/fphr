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
 * Logo upload IS wired to the real Documents module (Item 15) via `uploadDocumentFile`
 * (documents.service.ts): initiate-upload -> PUT bytes directly to storage -> confirm-upload.
 */
import { mockDelay, readMock, writeMock } from "@/services/mock/local-store";
import { uploadDocumentFile } from "@/services/documents.service";

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
 * if the upload itself fails (the profile screen has no real backend yet, see the file header).
 */
export async function uploadCompanyLogo(file: File): Promise<UploadLogoResult> {
  const objectUrl = URL.createObjectURL(file);

  try {
    const documentId = await uploadDocumentFile(file, "logos");
    await updateCompanyProfile({ logoDocumentId: documentId, logoUrl: objectUrl });
    return { documentId, logoUrl: objectUrl };
  } catch (error) {
    console.error("Logo upload failed, falling back to local preview only:", error);
    await updateCompanyProfile({ logoUrl: objectUrl });
    return { logoUrl: objectUrl };
  }
}
