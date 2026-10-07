namespace Sense.Modules.Leave.Domain.Calendar;

/// <summary>Calendar-date rules for leave types restricted to an employee's birthday.</summary>
public static class LeaveBirthdayPolicy
{
    public static bool IsBirthday(DateOnly birthDate, DateOnly requestedDate, LeaveFeb29Policy feb29Policy)
    {
        if (birthDate.Month != 2 || birthDate.Day != 29)
        {
            return requestedDate.Month == birthDate.Month && requestedDate.Day == birthDate.Day;
        }

        if (DateTime.IsLeapYear(requestedDate.Year))
        {
            return requestedDate.Month == 2 && requestedDate.Day == 29;
        }

        return feb29Policy switch
        {
            LeaveFeb29Policy.UseFebruary28 => requestedDate.Month == 2 && requestedDate.Day == 28,
            LeaveFeb29Policy.UseMarch1 => requestedDate.Month == 3 && requestedDate.Day == 1,
            _ => false,
        };
    }
}
