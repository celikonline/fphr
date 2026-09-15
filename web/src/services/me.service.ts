/**
 * Current user ("me") service - `GET /v1/identity/me` (MeController / MeDto in Identity.Application/Dtos.cs).
 */
import { apiClient } from "@senseik/api-client";
import type { MeFeatures } from "@/types";

export type DataScope = "Self" | "Team" | "Department" | "Location" | "Company" | "Custom";

export interface MeTenant {
  id: string;
  slug: string;
  name: string;
  taxNumber: string | null;
  status: string;
  trialEndsAt: string | null;
  locale: string;
  timeZone: string;
}

export interface MeRole {
  id: string;
  code: string;
  name: string | null;
}

export interface MeResponse {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeId: string | null;
  preferredLanguage: string;
  mfaEnabled: boolean;
  tenant: MeTenant;
  roles: MeRole[];
  permissions: string[];
  scopes: Record<string, DataScope>;
  /** Older backends may omit `accessSettingsBypass`; the auth store normalizes it. */
  features?: Partial<MeFeatures> | null;
}

export const ME_PATH = "/v1/identity/me";

export async function getMe(): Promise<MeResponse> {
  const { data } = await apiClient.get<MeResponse>(ME_PATH);
  return data;
}
