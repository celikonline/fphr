namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Company calendar entry classification (senseik-027, roadmap #71/P-19). <see cref="Birthday"/> and
/// <see cref="LeaveStart"/> are never stored - they are derived at query time (Employee.BirthDate via
/// IEmployeeBirthDateLookup, Leave's approved requests via ILeaveStartDateReader) - see
/// <see cref="CompanyEventDomainErrors.TypeNotManual"/>. Only <see cref="CompanyEvent"/> and <see cref="Meeting"/>
/// can be created manually through <see cref="Announcements.Application.CompanyEvents.CreateCompanyEventCommand"/>.
/// </summary>
public enum CompanyEventType
{
    /// <summary>Manually created, general company event (celebration, holiday party, ...).</summary>
    CompanyEvent = 0,

    /// <summary>Manually created meeting.</summary>
    Meeting = 1,

    /// <summary>Derived from Employee.BirthDate; never persisted as a CompanyEvent row.</summary>
    Birthday = 2,

    /// <summary>Derived from Leave's approved requests; never persisted as a CompanyEvent row.</summary>
    LeaveStart = 3,
}
