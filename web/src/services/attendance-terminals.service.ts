/**
 * PDKS terminalleri (attendance.terminal.view/manage), terminal grupları ve çalışan terminal kimlikleri.
 * Yollar/gövdeler T1 (TerminalsController, TerminalGroupsController) ve T2 (TerminalCredentialsController) ile hizalı.
 * Ham API anahtarı yalnız oluşturma/yenileme yanıtında bir kez döner; çağıran onu yalnız bellekte tutar.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/terminals";
const GROUPS = "/v1/attendance/terminal-groups";
const CREDENTIALS = "/v1/attendance/terminal-credentials";

export type TerminalDirectionMode = "both" | "inOnly" | "outOnly";
export const TERMINAL_DIRECTION_MODES: TerminalDirectionMode[] = ["both", "inOnly", "outOnly"];

export interface Terminal {
  id: string;
  code: string;
  name: string;
  terminalGroupId?: string | null;
  locationId?: string | null;
  directionMode: TerminalDirectionMode;
  isActive: boolean;
  lastSeenAtUtc?: string | null;
  apiKeyPrefix?: string;
  /** Sunucu hesaplar: son görülme < 5 dk. */
  isOnline: boolean;
  /** Son "şüpheli PIN denemesi" zamanı (yoksa alan gelmez). */
  lastSuspiciousAtUtc?: string | null;
}

/** POST terminals/heartbeat yanıtı (terminal anahtarıyla çağrılır; yönetim UI'ı çağırmaz, yalnız tip). */
export interface TerminalHeartbeatResult {
  pinEntryBlocked?: boolean;
}

export interface TerminalInput {
  code: string;
  name: string;
  terminalGroupId?: string | null;
  locationId?: string | null;
  directionMode: TerminalDirectionMode;
}

/** Ham anahtar yalnız burada bulunur; kalıcı hiçbir yere yazılmaz. */
export interface TerminalApiKeyResult {
  terminalId: string;
  apiKey: string;
  apiKeyPrefix: string;
}

export interface TerminalGroup {
  id: string;
  name: string;
}

export type TerminalCredentialKind = "card" | "pin" | "externalId";
export const TERMINAL_CREDENTIAL_KINDS: TerminalCredentialKind[] = ["card", "pin", "externalId"];

/** Listede değer ya da özet ASLA yok: yalnız tür, durum ve oluşturulma. */
export interface TerminalCredential {
  id: string;
  employeeId: string;
  kind: TerminalCredentialKind;
  isActive: boolean;
  createdAt?: string;
}

export async function listTerminals(): Promise<Terminal[]> {
  const { data } = await apiClient.get<Terminal[]>(BASE);
  return data;
}

export async function createTerminal(input: TerminalInput): Promise<TerminalApiKeyResult> {
  const { data } = await apiClient.post<TerminalApiKeyResult>(BASE, input);
  return data;
}

export async function updateTerminal(id: string, input: TerminalInput): Promise<void> {
  await apiClient.put(`${BASE}/${id}`, input);
}

export async function rotateTerminalApiKey(id: string): Promise<TerminalApiKeyResult> {
  const { data } = await apiClient.post<TerminalApiKeyResult>(`${BASE}/${id}/api-key`);
  return data;
}

export async function setTerminalActive(id: string, isActive: boolean): Promise<void> {
  await apiClient.put(`${BASE}/${id}/active`, { isActive });
}

export async function listTerminalGroups(): Promise<TerminalGroup[]> {
  const { data } = await apiClient.get<TerminalGroup[]>(GROUPS);
  return data;
}

/** Yanıt yalnız yeni grubun Guid'i. */
export async function createTerminalGroup(name: string): Promise<string> {
  const { data } = await apiClient.post<string>(GROUPS, { name });
  return data;
}

export async function renameTerminalGroup(id: string, name: string): Promise<void> {
  await apiClient.put(`${GROUPS}/${id}`, { name });
}

export async function deleteTerminalGroup(id: string): Promise<void> {
  await apiClient.delete(`${GROUPS}/${id}`);
}

export async function listEmployeeCredentials(employeeId: string): Promise<TerminalCredential[]> {
  const { data } = await apiClient.get<TerminalCredential[]>(CREDENTIALS, { params: { employeeId } });
  return data;
}

export async function addEmployeeCredential(input: {
  employeeId: string;
  kind: TerminalCredentialKind;
  value: string;
}): Promise<void> {
  await apiClient.post(CREDENTIALS, input);
}

export async function setCredentialActive(id: string, active: boolean): Promise<void> {
  await apiClient.post(`${CREDENTIALS}/${id}/${active ? "activate" : "deactivate"}`);
}
