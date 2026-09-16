import { apiClient } from "@/lib/apiClient";

/**
 * Profile module (self-service, read-only): mirrors the `GET /employees/me` endpoint of
 * `EmployeeController.cs`, the same one `web/src/services/employees.service.ts`'s `getMyEmployee()`
 * calls. Only the fields the profile screen shows are typed here (name, title/position, org unit,
 * hire date, work e-mail, phone) - sensitive fields (national id, salary, address, etc.) exist on the
 * backend DTO but are intentionally left out (KVKK data minimization, no edit surface either).
 */
const EMPLOYEES_BASE = "/api/v1/employees";

/** Subset of `EmployeeDetail` (`web/src/services/employees.service.ts`) the read-only profile card shows. */
export interface MyProfile {
  displayName: string;
  titleName?: string | null;
  departmentName?: string | null;
  hireDate: string | null;
  workEmail: string | null;
  mobilePhone?: string | null;
  workPhone: string | null;
}

export class ProfileService {
  async getMyProfile(): Promise<MyProfile> {
    const response = await apiClient.get<MyProfile>(`${EMPLOYEES_BASE}/me`);
    return response.data;
  }
}

export const profileService = new ProfileService();
