/** Onay vekaletleri (workflow.delegation.manage, workflow.my.delegation). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/workflow/delegations";

export interface Delegation {
  id: string;
  delegatorUserId: string;
  delegateUserId: string;
  /** "yyyy-MM-dd". */
  from: string;
  to: string;
  isActive: boolean;
  /** Optional display names when the API resolves them. */
  delegatorName?: string;
  delegateName?: string;
}

export interface CreateDelegationInput {
  delegateUserId: string;
  from: string;
  to: string;
}

export async function listDelegations(): Promise<Delegation[]> {
  const { data } = await apiClient.get<Delegation[]>(BASE);
  return data;
}

export async function createDelegation(dto: CreateDelegationInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(BASE, dto);
  return data.id;
}

export async function revokeDelegation(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}
