/**
 * Home (Ana sayfa) widget reads. Kept separate from employees/structure services, which other packages own.
 */
import { apiClient } from "@senseik/api-client";

/** Mirrors `UpcomingHolidayDto` (Organization, contract P3). `isHalfDay` may be absent on older API
 * responses - treat missing as a full day. */
export interface UpcomingHoliday {
  date: string;
  name: string;
  holidayCalendarId: string;
  calendarName: string;
  isHalfDay?: boolean;
}

/** Mirrors `UpcomingBirthdayDto` (Employee, contract P1). */
export interface UpcomingBirthday {
  id: string;
  displayName: string;
  orgUnitId: string | null;
  nextBirthday: string;
}

export async function getUpcomingHolidays(days = 90): Promise<UpcomingHoliday[]> {
  const { data } = await apiClient.get<UpcomingHoliday[]>(
    "/v1/organization/holiday-calendars/upcoming",
    {
      params: { days },
    }
  );
  return data;
}

export async function getUpcomingBirthdays(days = 30): Promise<UpcomingBirthday[]> {
  const { data } = await apiClient.get<UpcomingBirthday[]>("/v1/employees/upcoming-birthdays", {
    params: { days },
  });
  return data;
}
