/** Çalışan notları (employee.notes.view/manage). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const employeeNotesBase = (employeeId: string) => `/v1/employees/${employeeId}/notes`;

export interface EmployeeNote {
  id: string;
  employeeId: string;
  text: string;
  createdAt: string;
  createdByName?: string | null;
}

export async function listEmployeeNotes(employeeId: string): Promise<EmployeeNote[]> {
  const { data } = await apiClient.get<EmployeeNote[]>(employeeNotesBase(employeeId));
  return data;
}

export async function addEmployeeNote(employeeId: string, text: string): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(employeeNotesBase(employeeId), { text });
  return data.id;
}
