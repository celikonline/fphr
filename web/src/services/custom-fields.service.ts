/**
 * Custom field definitions (E4, "Özel alan tanımı genişletme", 11.3.03): tenant admin catalog.
 * Mirrors `CustomFieldDefinitionsController` (`api/v1/employees/custom-field-definitions`).
 * Employee-level values are read/written through `employees.service.ts` instead.
 */
import { apiClient } from "@senseik/api-client";

const DEFINITIONS_BASE = "/v1/employees/custom-field-definitions";

/** Mirrors `CustomFieldDataType` (camelCase enum JSON). */
export type CustomFieldDataType = "text" | "number" | "date" | "boolean" | "singleSelect" | "multiSelect";
export const CUSTOM_FIELD_DATA_TYPES: CustomFieldDataType[] = ["text", "number", "date", "boolean", "singleSelect", "multiSelect"];

/** Mirrors `CustomFieldSection` (camelCase enum JSON). */
export type CustomFieldSection = "personal" | "career" | "other";
export const CUSTOM_FIELD_SECTIONS: CustomFieldSection[] = ["personal", "career", "other"];

/** True for the two data types that need a non-empty `options` list. */
export function isSelectDataType(dataType: CustomFieldDataType): boolean {
  return dataType === "singleSelect" || dataType === "multiSelect";
}

export interface CustomFieldDefinition {
  id: string;
  key: string;
  name: string;
  dataType: CustomFieldDataType;
  section: CustomFieldSection;
  options: string[];
  isRequired: boolean;
  isEmployeeEditable: boolean;
  employeeEditRequiresApproval: boolean;
  isReportable: boolean;
  isSensitive: boolean;
  sortOrder: number;
  isActive: boolean;
}

/** Create/update body (`CustomFieldDefinitionRequest`); on update, `key`/`dataType` are sent but ignored by the server (both immutable once created). */
export interface CustomFieldDefinitionInput {
  key: string;
  name: string;
  dataType: CustomFieldDataType;
  section: CustomFieldSection;
  options: string[] | null;
  isRequired: boolean;
  isEmployeeEditable: boolean;
  employeeEditRequiresApproval: boolean;
  isReportable: boolean;
  isSensitive: boolean;
  sortOrder: number;
}

export async function listCustomFieldDefinitions(includeInactive = false): Promise<CustomFieldDefinition[]> {
  const { data } = await apiClient.get<CustomFieldDefinition[]>(DEFINITIONS_BASE, { params: { includeInactive } });
  return data;
}

export async function createCustomFieldDefinition(input: CustomFieldDefinitionInput): Promise<CustomFieldDefinition> {
  const { data } = await apiClient.post<CustomFieldDefinition>(DEFINITIONS_BASE, input);
  return data;
}

export async function updateCustomFieldDefinition(id: string, input: CustomFieldDefinitionInput): Promise<CustomFieldDefinition> {
  const { data } = await apiClient.put<CustomFieldDefinition>(`${DEFINITIONS_BASE}/${id}`, input);
  return data;
}

export async function deactivateCustomFieldDefinition(id: string): Promise<CustomFieldDefinition> {
  const { data } = await apiClient.post<CustomFieldDefinition>(`${DEFINITIONS_BASE}/${id}/deactivate`);
  return data;
}

export async function reactivateCustomFieldDefinition(id: string): Promise<CustomFieldDefinition> {
  const { data } = await apiClient.post<CustomFieldDefinition>(`${DEFINITIONS_BASE}/${id}/reactivate`);
  return data;
}
