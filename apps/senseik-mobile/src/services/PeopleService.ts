import { apiClient } from "@/lib/apiClient";

/**
 * People directory module (Kolay İK parity, TK-024): mirrors the existing `GET /employees/directory`
 * endpoint of `EmployeesController.cs` (`employee.directory.view`), the same one
 * `web/src/services/employees.service.ts`'s `getEmployeeDirectory()` calls. No new backend endpoint -
 * this reuses the company directory query, which already resolves Title/Department server-side so
 * callers without `organization.units.read` still see them, and gates `workPhone` behind the caller's
 * "show work phone numbers" access setting.
 */
const EMPLOYEES_BASE = "/api/v1/employees";

/** Subset of `DirectoryEntryDto` (`web/src/services/employees.service.ts`'s `DirectoryEntry`) the people list shows. */
export interface PersonListItem {
  id: string;
  displayName: string;
  titleName?: string | null;
  departmentName?: string | null;
  workEmail: string | null;
  /** Null when the caller's "show work phone numbers" access setting is off. */
  workPhone: string | null;
}

/** Mirrors the backend's `PagedResult<T>` (Sense.Shared.Contracts.Paging) as camelCase JSON. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

export interface ListPeopleParams {
  q?: string;
  page?: number;
  pageSize?: number;
}

export class PeopleService {
  async listPeople(params: ListPeopleParams = {}): Promise<PagedResult<PersonListItem>> {
    const response = await apiClient.get<PagedResult<PersonListItem>>(
      `${EMPLOYEES_BASE}/directory`,
      {
        params: {
          q: params.q || undefined,
          page: params.page,
          pageSize: params.pageSize,
        },
      }
    );
    return response.data;
  }
}

export const peopleService = new PeopleService();
