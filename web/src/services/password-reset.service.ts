/**
 * Password reset (Faz 3, item 1): `POST /v1/identity/auth/forgot-password` (anonymous, always 204 — no
 * enumeration) and `POST /v1/identity/auth/reset-password` (anonymous, token from the email link).
 * Mirrors the invitation accept flow in `invitations.service.ts`.
 */
import { apiClient } from "@senseik/api-client";

const AUTH_BASE = "/v1/identity/auth";

export async function forgotPassword(email: string): Promise<void> {
  await apiClient.post(`${AUTH_BASE}/forgot-password`, { email });
}

export interface ResetPasswordInput {
  token: string;
  newPassword: string;
}

export async function resetPassword(input: ResetPasswordInput): Promise<void> {
  await apiClient.post(`${AUTH_BASE}/reset-password`, input);
}
