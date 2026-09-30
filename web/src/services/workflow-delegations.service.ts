/**
 * Onay vekaletleri (Z2.2). "Vekaletlerim" uçları /mine (workflow.my.delegation, çağıranın kendi adına);
 * yönetici uçları kökte (workflow.delegation.manage, herhangi bir çalışan adına).
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/workflow/delegations";

export interface Delegation {
  id: string;
  delegatorEmployeeId: string;
  delegateEmployeeId: string;
  delegatorName?: string;
  delegateName?: string;
  /** "yyyy-MM-dd". */
  startDate: string;
  endDate: string;
  /** Empty = every request type. */
  requestTypes: string[];
  isRevoked: boolean;
  revokedAt?: string;
}

export interface CreateMyDelegationInput {
  delegateEmployeeId: string;
  startDate: string;
  endDate: string;
  requestTypes?: string[];
}

export interface CreateDelegationInput extends CreateMyDelegationInput {
  delegatorEmployeeId: string;
}

/** Delegations the caller gave or received. */
export async function listMyDelegations(): Promise<Delegation[]> {
  const { data } = await apiClient.get<Delegation[]>(`${BASE}/mine`);
  return data;
}

export async function createMyDelegation(dto: CreateMyDelegationInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/mine`, dto);
  return data;
}

export async function revokeMyDelegation(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/mine/${id}`);
}

/** Admin: every delegation in the tenant. */
export async function listDelegations(): Promise<Delegation[]> {
  const { data } = await apiClient.get<Delegation[]>(BASE);
  return data;
}

export async function createDelegation(dto: CreateDelegationInput): Promise<string> {
  const { data } = await apiClient.post<string>(BASE, dto);
  return data;
}

export async function revokeDelegation(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}
