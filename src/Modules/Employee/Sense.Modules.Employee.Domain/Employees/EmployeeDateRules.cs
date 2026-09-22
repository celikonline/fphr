namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Plausibility bounds on birth/hire dates shared by <see cref="Employee.Validate"/> and the FluentValidation mirror
/// (one source, so the per-field 400 and the domain guard cannot drift). Complements the senseik-044 rules
/// (birth not in the future, birth not after hire).
/// </summary>
public static class EmployeeDateRules
{
    /// <summary>Earliest plausible birth/hire date; anything before is a data-entry error (e.g. 1850).</summary>
    public static readonly DateOnly EarliestDate = new(1900, 1, 1);

    /// <summary>4857 s. İş Kanunu md.71 / Çocuk ve Genç İşçiler Yönetmeliği: nobody under 15 may be employed.</summary>
    public const int MinimumEmploymentAgeYears = 15;

    /// <summary>A hire date may be scheduled at most this many months ahead (planned starts; not years out).</summary>
    public const int MaxHireDateMonthsAhead = 12;

    /// <summary>An exit may be recorded at most this many months ahead (notice periods are weeks, not years).</summary>
    public const int MaxTerminationDateMonthsAhead = 6;

    public static string? CheckTerminationDate(DateOnly terminationDate, DateOnly today) =>
        terminationDate > today.AddMonths(MaxTerminationDateMonthsAhead) ? EmployeeDomainErrors.TerminationDateTooFarInFuture : null;

    public static string? CheckBirthDate(DateOnly? birthDate) =>
        birthDate is { } b && b < EarliestDate ? EmployeeDomainErrors.BirthDateTooEarly : null;

    public static string? CheckHireDate(DateOnly? hireDate, DateOnly today)
    {
        if (hireDate is not { } h)
        {
            return null;
        }

        if (h < EarliestDate)
        {
            return EmployeeDomainErrors.HireDateTooEarly;
        }

        return h > today.AddMonths(MaxHireDateMonthsAhead) ? EmployeeDomainErrors.HireDateTooFarInFuture : null;
    }

    /// <summary>Age in completed years on the hire date must be at least <see cref="MinimumEmploymentAgeYears"/> (15-17 is allowed).</summary>
    public static string? CheckMinimumAge(DateOnly? birthDate, DateOnly? hireDate) =>
        birthDate is { } b && hireDate is { } h && b.AddYears(MinimumEmploymentAgeYears) > h
            ? EmployeeDomainErrors.MinimumAge
            : null;
}
