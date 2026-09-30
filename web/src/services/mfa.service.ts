/**
 * MFA enrollment/verification (Faz 3 tur 3, S-5 sözleşmesi — `senseik-takim/status/faz-03/PANO.md`).
 * `POST /v1/identity/me/mfa/enroll` (auth) -> { secret, otpAuthUri }; `MfaEnabled` stays false until verified.
 * `POST /v1/identity/me/mfa/verify` { code } -> 204, enables MFA.
 * `POST /v1/identity/me/mfa/disable` { code } -> 204.
 * `GET /v1/identity/me/mfa` -> { enabled, pendingEnrollment, requiredByPolicy } (S-12, gün açılışı tur 1): security
 * screen's source of truth on load, in particular `pendingEnrollment` (secret written but never verified) which
 * `/identity/me` doesn't expose.
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

export interface MfaStatus {
  enabled: boolean;
  /** Secret was generated (`enroll` called) but never verified — the enroll dialog can be reopened to resume. */
  pendingEnrollment: boolean;
  /**
   * FG-022: the tenant's security policy requires MFA for one of the caller's roles - `disable` then answers 400
   * `identity.mfa.required_by_policy`, so the security screen disables its "turn off" button. Optional: an older API
   * omits it, which reads as "not required".
   */
  requiredByPolicy?: boolean;
}

export async function getMfaStatus(): Promise<MfaStatus> {
  const { data } = await apiClient.get<MfaStatus>(`${ME_PATH}/mfa`);
  return data;
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
