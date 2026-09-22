using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Domain.Incidents;

/// <summary>İSG olayı türü: iş kazası (SGK'ya bildirimi zorunlu) ya da ramak kala (yaralanmasız tehlikeli olay).</summary>
public enum OhsIncidentKind
{
    WorkAccident = 0,
    NearMiss = 1,
}

/// <summary>
/// İş kazası / ramak kala kaydı (senseik-015 Faz 3, 6331 sayılı Kanun md.14). A work accident carries the SGK
/// notification deadline (5510 md.13: the third business day after the accident, computed by the caller with the
/// tenant's holiday calendar and passed in, since the Domain has no calendar access) and records the date it was
/// actually notified. Either kind is closed once a corrective action ("düzeltici önlem") is written down.
/// Location and employee are cross-module Guid references (Organization / Employee), same pattern as RiskAssessment.
/// </summary>
public sealed class OhsIncident : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OhsIncident()
    {
    }

    public OhsIncidentKind Kind { get; private set; }
    public DateOnly EventDate { get; private set; }
    public Guid? LocationId { get; private set; }

    /// <summary>The injured / involved employee; null for a near miss nobody was hurt in.</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>Employee who reported it through self-service; null when HR recorded it directly.</summary>
    public Guid? ReportedByEmployeeId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    /// <summary>Last day to notify SGK (work accidents only).</summary>
    public DateOnly? SgkNotificationDeadline { get; private set; }

    public DateOnly? SgkNotifiedOn { get; private set; }
    public string? CorrectiveAction { get; private set; }
    public DateOnly? ClosedOn { get; private set; }

    public bool IsClosed => ClosedOn is not null;

    public static Result<OhsIncident> Create(
        Guid tenantId,
        OhsIncidentKind kind,
        DateOnly eventDate,
        DateOnly today,
        Guid? locationId,
        Guid? employeeId,
        Guid? reportedByEmployeeId,
        string description,
        DateOnly? sgkNotificationDeadline)
    {
        if (!Enum.IsDefined(kind))
        {
            return Error.Validation(OhsDomainErrors.IncidentKindInvalid);
        }

        if (eventDate > today)
        {
            return Error.Validation(OhsDomainErrors.IncidentDateInFuture);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation(OhsDomainErrors.IncidentDescriptionRequired);
        }

        if (kind == OhsIncidentKind.WorkAccident && employeeId is null)
        {
            return Error.Validation(OhsDomainErrors.IncidentEmployeeRequired);
        }

        if (kind == OhsIncidentKind.WorkAccident && sgkNotificationDeadline is null)
        {
            return Error.Validation(OhsDomainErrors.IncidentSgkDeadlineRequired);
        }

        return new OhsIncident
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Kind = kind,
            EventDate = eventDate,
            LocationId = locationId,
            EmployeeId = employeeId,
            ReportedByEmployeeId = reportedByEmployeeId,
            Description = Guard.MaxLength(description.Trim(), OhsLimits.IncidentDescriptionMaxLength),
            SgkNotificationDeadline = kind == OhsIncidentKind.WorkAccident ? sgkNotificationDeadline : null,
        };
    }

    /// <summary>Records the date the accident was notified to SGK (work accidents only, not before the accident).</summary>
    public Result MarkSgkNotified(DateOnly notifiedOn, DateOnly today)
    {
        if (Kind != OhsIncidentKind.WorkAccident)
        {
            return Error.Rule(OhsDomainErrors.IncidentSgkNotApplicable);
        }

        if (notifiedOn < EventDate || notifiedOn > today)
        {
            return Error.Validation(OhsDomainErrors.IncidentSgkDateInvalid);
        }

        SgkNotifiedOn = notifiedOn;
        return Result.Success();
    }

    /// <summary>Closes the incident with its corrective action; an unnotified work accident cannot be closed.</summary>
    public Result Close(string correctiveAction, DateOnly today)
    {
        if (IsClosed)
        {
            return Error.Conflict(OhsDomainErrors.IncidentAlreadyClosed);
        }

        if (string.IsNullOrWhiteSpace(correctiveAction))
        {
            return Error.Validation(OhsDomainErrors.IncidentCorrectiveActionRequired);
        }

        if (Kind == OhsIncidentKind.WorkAccident && SgkNotifiedOn is null)
        {
            return Error.Rule(OhsDomainErrors.IncidentSgkNotNotified);
        }

        CorrectiveAction = Guard.MaxLength(correctiveAction.Trim(), OhsLimits.IncidentDescriptionMaxLength);
        ClosedOn = today;
        return Result.Success();
    }
}
