using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.Services;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.Services;

public sealed record SafetyAssignmentDto(
    Guid Id, Guid LocationId, SafetyServiceRole Role, string PersonName, Guid? EmployeeId,
    SafetyExpertCertificateClass? CertificateClass, string? CertificateNumber, string? ProviderName,
    int MonthlyMinutes, DateOnly StartDate, DateOnly? EndDate);

public sealed record CommitteeMemberDto(
    Guid Id, Guid LocationId, CommitteeRole Role, string PersonName, Guid? EmployeeId, DateOnly StartDate, DateOnly? EndDate);

/// <summary>One role's statutory minutes vs the minutes contracted by active assignments.</summary>
public sealed record ServiceMinutesDto(SafetyServiceRole Role, int RequiredMinutes, int AssignedMinutes)
{
    public bool IsMet => AssignedMinutes >= RequiredMinutes;
}

/// <summary>
/// A classified location's OHS service picture today. Issue codes (web translates them): missing_* (a mandated role
/// has no assignment), insufficient_minutes_*, certificate_class (an active expert's class is below the workplace's),
/// committee_missing (50+ employees and no active chair/secretary), representatives (fewer representatives than 6331 md.20).
/// </summary>
public sealed record LocationServiceSummaryDto(
    Guid LocationId,
    HazardClass HazardClass,
    int EmployeeCount,
    IReadOnlyList<ServiceMinutesDto> Minutes,
    bool CommitteeRequired,
    int CommitteeMeetingIntervalMonths,
    int CommitteeMemberCount,
    int RequiredRepresentativeCount,
    int RepresentativeCount,
    IReadOnlyList<string> Issues);

internal static class OhsServiceMapper
{
    public static SafetyAssignmentDto ToDto(SafetyAssignment a) => new(
        a.Id, a.LocationId, a.Role, a.PersonName, a.EmployeeId, a.CertificateClass, a.CertificateNumber, a.ProviderName,
        a.MonthlyMinutes, a.StartDate, a.EndDate);

    public static CommitteeMemberDto ToDto(OhsCommitteeMember m) =>
        new(m.Id, m.LocationId, m.Role, m.PersonName, m.EmployeeId, m.StartDate, m.EndDate);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

// ---------------------------------------------------------------- commands

[RequiresPermission("ohs.services.manage")]
public sealed record CreateSafetyAssignmentCommand(
    Guid LocationId, SafetyServiceRole Role, string PersonName, Guid? EmployeeId,
    SafetyExpertCertificateClass? CertificateClass, string? CertificateNumber, string? ProviderName,
    int MonthlyMinutes, DateOnly StartDate) : ICommand<Guid>;

public sealed class CreateSafetyAssignmentValidator : AbstractValidator<CreateSafetyAssignmentCommand>
{
    public CreateSafetyAssignmentValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.CertificateClass).IsInEnum().When(x => x.CertificateClass is not null);
        RuleFor(x => x.PersonName).NotEmpty().MaximumLength(OhsLimits.PersonNameMaxLength);
        RuleFor(x => x.CertificateNumber).MaximumLength(OhsLimits.PersonNameMaxLength);
        RuleFor(x => x.ProviderName).MaximumLength(OhsLimits.PersonNameMaxLength);
    }
}

public sealed class CreateSafetyAssignmentHandler(ITenantContext tenant, ISafetyAssignmentRepository assignments)
    : ICommandHandler<CreateSafetyAssignmentCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateSafetyAssignmentCommand c, CancellationToken cancellationToken)
    {
        var created = SafetyAssignment.Create(
            tenant.TenantId, c.LocationId, c.Role, c.PersonName, c.EmployeeId, c.CertificateClass, c.CertificateNumber,
            c.ProviderName, c.MonthlyMinutes, c.StartDate);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<Guid>>(created.Error);
        }

        assignments.Add(created.Value);
        return Task.FromResult<Result<Guid>>(created.Value.Id);
    }
}

[RequiresPermission("ohs.services.manage")]
public sealed record EndSafetyAssignmentCommand(Guid AssignmentId, DateOnly EndDate) : ICommand;

public sealed class EndSafetyAssignmentHandler(ISafetyAssignmentRepository assignments) : ICommandHandler<EndSafetyAssignmentCommand>
{
    public async Task<Result> Handle(EndSafetyAssignmentCommand c, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByIdAsync(c.AssignmentId, cancellationToken);
        return assignment.IsFailure ? assignment.Error : assignment.Value.End(c.EndDate);
    }
}

[RequiresPermission("ohs.services.manage")]
public sealed record AddCommitteeMemberCommand(Guid LocationId, CommitteeRole Role, string PersonName, Guid? EmployeeId, DateOnly StartDate)
    : ICommand<Guid>;

public sealed class AddCommitteeMemberValidator : AbstractValidator<AddCommitteeMemberCommand>
{
    public AddCommitteeMemberValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.PersonName).NotEmpty().MaximumLength(OhsLimits.PersonNameMaxLength);
    }
}

public sealed class AddCommitteeMemberHandler(ITenantContext tenant, IOhsCommitteeMemberRepository members)
    : ICommandHandler<AddCommitteeMemberCommand, Guid>
{
    public Task<Result<Guid>> Handle(AddCommitteeMemberCommand c, CancellationToken cancellationToken)
    {
        var created = OhsCommitteeMember.Create(tenant.TenantId, c.LocationId, c.Role, c.PersonName, c.EmployeeId, c.StartDate);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<Guid>>(created.Error);
        }

        members.Add(created.Value);
        return Task.FromResult<Result<Guid>>(created.Value.Id);
    }
}

[RequiresPermission("ohs.services.manage")]
public sealed record EndCommitteeMemberCommand(Guid MemberId, DateOnly EndDate) : ICommand;

public sealed class EndCommitteeMemberHandler(IOhsCommitteeMemberRepository members) : ICommandHandler<EndCommitteeMemberCommand>
{
    public async Task<Result> Handle(EndCommitteeMemberCommand c, CancellationToken cancellationToken)
    {
        var member = await members.GetByIdAsync(c.MemberId, cancellationToken);
        return member.IsFailure ? member.Error : member.Value.End(c.EndDate);
    }
}

// ---------------------------------------------------------------- queries

[RequiresPermission("ohs.services.view")]
public sealed record ListSafetyAssignmentsQuery : IQuery<IReadOnlyList<SafetyAssignmentDto>>;

public sealed class ListSafetyAssignmentsHandler(ISafetyAssignmentRepository assignments)
    : IQueryHandler<ListSafetyAssignmentsQuery, IReadOnlyList<SafetyAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<SafetyAssignmentDto>>> Handle(ListSafetyAssignmentsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SafetyAssignmentDto> items = (await assignments.ListAsync(cancellationToken)).Select(OhsServiceMapper.ToDto).ToList();
        return Result.Success(items);
    }
}

[RequiresPermission("ohs.services.view")]
public sealed record ListCommitteeMembersQuery : IQuery<IReadOnlyList<CommitteeMemberDto>>;

public sealed class ListCommitteeMembersHandler(IOhsCommitteeMemberRepository members)
    : IQueryHandler<ListCommitteeMembersQuery, IReadOnlyList<CommitteeMemberDto>>
{
    public async Task<Result<IReadOnlyList<CommitteeMemberDto>>> Handle(ListCommitteeMembersQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<CommitteeMemberDto> items = (await members.ListAsync(cancellationToken)).Select(OhsServiceMapper.ToDto).ToList();
        return Result.Success(items);
    }
}

/// <summary>Service compliance for every location that has a hazard class today (unclassified locations have no norms).</summary>
[RequiresPermission("ohs.services.view")]
public sealed record ListLocationServiceSummariesQuery : IQuery<IReadOnlyList<LocationServiceSummaryDto>>;

public sealed class ListLocationServiceSummariesHandler(
    ITenantContext tenant,
    IWorkplaceHazardClassificationRepository classifications,
    ISafetyAssignmentRepository assignments,
    IOhsCommitteeMemberRepository members,
    IOrgUnitLookup orgUnits,
    IEmployeeLookup employees,
    TimeProvider clock) : IQueryHandler<ListLocationServiceSummariesQuery, IReadOnlyList<LocationServiceSummaryDto>>
{
    public async Task<Result<IReadOnlyList<LocationServiceSummaryDto>>> Handle(
        ListLocationServiceSummariesQuery query, CancellationToken cancellationToken)
    {
        var today = OhsServiceMapper.Today(clock);
        var classified = await classifications.ListCurrentForAllLocationsAsync(today, cancellationToken);
        var unitLocations = await orgUnits.GetEffectiveLocationMapAsync(tenant.TenantId, cancellationToken);
        var perUnit = await employees.CountActiveByOrgUnitAsync(tenant.TenantId, cancellationToken);
        var headcount = perUnit
            .Where(u => unitLocations.ContainsKey(u.Key))
            .GroupBy(u => unitLocations[u.Key])
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Value));
        var activeAssignments = (await assignments.ListAsync(cancellationToken)).Where(a => a.IsActiveOn(today)).ToList();
        var activeMembers = (await members.ListAsync(cancellationToken)).Where(m => m.IsActiveOn(today)).ToList();

        IReadOnlyList<LocationServiceSummaryDto> items = classified
            .Select(c => Summarize(c.LocationId, c.HazardClass, headcount.GetValueOrDefault(c.LocationId),
                activeAssignments.Where(a => a.LocationId == c.LocationId).ToList(),
                activeMembers.Where(m => m.LocationId == c.LocationId).ToList()))
            .ToList();
        return Result.Success(items);
    }

    /// <summary>Pure norm check for one location (public for tests).</summary>
    public static LocationServiceSummaryDto Summarize(
        Guid locationId, HazardClass hazardClass, int employeeCount,
        IReadOnlyList<SafetyAssignment> assignments, IReadOnlyList<OhsCommitteeMember> members)
    {
        var issues = new List<string>();
        var minutes = Enum.GetValues<SafetyServiceRole>()
            .Select(role => new ServiceMinutesDto(
                role,
                OhsServiceNorms.MinutesPerEmployeePerMonth(role, hazardClass) * employeeCount,
                assignments.Where(a => a.Role == role).Sum(a => a.MonthlyMinutes)))
            .Where(m => m.RequiredMinutes > 0 || m.AssignedMinutes > 0)
            .ToList();
        foreach (var m in minutes.Where(m => m.RequiredMinutes > 0))
        {
            var key = m.Role switch
            {
                SafetyServiceRole.SafetyExpert => "safety_expert",
                SafetyServiceRole.WorkplacePhysician => "physician",
                _ => "other_health_personnel",
            };
            if (m.AssignedMinutes == 0)
            {
                issues.Add($"missing_{key}");
            }
            else if (!m.IsMet)
            {
                issues.Add($"insufficient_minutes_{key}");
            }
        }

        if (assignments.Any(a => a.Role == SafetyServiceRole.SafetyExpert
                && a.CertificateClass is { } cls && !OhsServiceNorms.CertificateAllowed(cls, hazardClass)))
        {
            issues.Add("certificate_class");
        }

        var committeeRequired = employeeCount >= OhsServiceNorms.CommitteeEmployeeThreshold;
        if (committeeRequired
            && !(members.Any(m => m.Role == CommitteeRole.Chair) && members.Any(m => m.Role == CommitteeRole.Secretary)))
        {
            issues.Add("committee_missing");
        }

        var requiredRepresentatives = OhsServiceNorms.EmployeeRepresentativeCount(employeeCount);
        var representatives = members.Count(m => m.Role == CommitteeRole.EmployeeRepresentative);
        if (committeeRequired && representatives < requiredRepresentatives)
        {
            issues.Add("representatives");
        }

        return new LocationServiceSummaryDto(
            locationId, hazardClass, employeeCount, minutes, committeeRequired,
            OhsServiceNorms.CommitteeMeetingIntervalMonths(hazardClass), members.Count,
            requiredRepresentatives, representatives, issues);
    }
}
