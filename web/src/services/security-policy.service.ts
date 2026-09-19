/**
 * Security policy (senseik-051) - Ayarlar > Sistem ayarları > Güvenlik politikası.
 * `GET/PUT /v1/identity/settings/security` (SecurityPolicyController); PUT requires `identity.security.manage`.
 */
import { apiClient } from "@senseik/api-client";
import type { SecurityPolicy } from "@/types";

export type { SecurityPolicy } from "@/types";

const SECURITY_POLICY_PATH = "/v1/identity/settings/security";

export async function getSecurityPolicy(): Promise<SecurityPolicy> {
  const { data } = await apiClient.get<SecurityPolicy>(SECURITY_POLICY_PATH);
  return data;
}

export async function updateSecurityPolicy(policy: SecurityPolicy): Promise<void> {
  await apiClient.put(SECURITY_POLICY_PATH, policy);
}
