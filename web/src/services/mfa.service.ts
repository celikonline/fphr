/**
 * MFA enrollment/verification (Faz 3 tur 3, S-5 sözleşmesi — `senseik-takim/status/faz-03/PANO.md`).
 * `POST /v1/identity/me/mfa/enroll` (auth) -> { secret, otpAuthUri }; `MfaEnabled` stays false until verified.
 * `POST /v1/identity/me/mfa/verify` { code } -> 204, enables MFA.
 * `POST /v1/identity/me/mfa/disable` { code } -> 204.
 * Login-time completion (`auth/mfa/complete`) lives in `auth.store.ts` next to `login()` since it shares the
 * same "establish session" logic.
 */
import { apiClient } from "@senseik/api-client";
import { ME_PATH } from "@/services/me.service";

export interface MfaEnrollment {
  secret: string;
  /** `MfaEnrollmentDto.OtpAuthUri` -> camelCase `otpAuthUri` (System.Text.Json CamelCase only lowercases the leading char). */
  otpAuthUri: string;
}

export async function enrollMfa(): Promise<MfaEnrollment> {
  const { data } = await apiClient.post<MfaEnrollment>(`${ME_PATH}/mfa/enroll`);
  return data;
}

export async function verifyMfaEnrollment(code: string): Promise<void> {
  await apiClient.post(`${ME_PATH}/mfa/verify`, { code });
}

export async function disableMfa(code: string): Promise<void> {
  await apiClient.post(`${ME_PATH}/mfa/disable`, { code });
}
