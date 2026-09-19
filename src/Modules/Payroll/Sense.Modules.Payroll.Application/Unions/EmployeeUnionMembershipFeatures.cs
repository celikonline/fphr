using FluentValidation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Unions;

public sealed record EmployeeUnionMembershipDto(
    Guid Id,
    Guid EmployeeId,
    Guid UnionId,
    DateOnly MembershipDate,
    DateOnly NotifiedToEmployerAt,
    UnionCoverageType CoverageType,
    UnionDuesRule DuesRule,
    decimal DuesValue,
    DateOnly? EndDate);

internal static class EmployeeUnionMembershipMapper
{
    public static EmployeeUnionMembershipDto ToDto(EmployeeUnionMembership m) => new(
        m.Id, m.EmployeeId, m.UnionId, m.MembershipDate, m.NotifiedToEmployerAt, m.CoverageType, m.DuesRule, m.DuesValue, m.EndDate);
}

/// <summary>Every union-membership record of the employee, newest first (11.3.07-bordro.md çalışan kartı "Sendika" alt bölümü).</summary>
[RequiresPermission("payroll.union.manage")]
public sealed record ListEmployeeUnionMembershipsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeUnionMembershipDto>>;

public sealed class ListEmployeeUnionMembershipsHandler(IEmployeeUnionMembershipRepository memberships)
    : IQueryHandler<ListEmployeeUnionMembershipsQuery, IReadOnlyList<EmployeeUnionMembershipDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeUnionMembershipDto>>> Handle(ListEmployeeUnionMembershipsQuery query, CancellationToken cancellationToken) =>
        (await memberships.ListByEmployeeAsync(query.EmployeeId, cancellationToken)).Select(EmployeeUnionMembershipMapper.ToDto).ToList();
}

/// <summary>The employee's union membership in effect on <paramref name="AsOf"/> (defaults to today, UTC).</summary>
[RequiresPermission("payroll.union.manage")]
public sealed record GetCurrentEmployeeUnionMembershipQuery(Guid EmployeeId, DateOnly? AsOf = null) : IQuery<EmployeeUnionMembershipDto>;

public sealed class GetCurrentEmployeeUnionMembershipHandler(IEmployeeUnionMembershipRepository memberships)
    : IQueryHandler<GetCurrentEmployeeUnionMembershipQuery, EmployeeUnionMembershipDto>
{
    public async Task<Result<EmployeeUnionMembershipDto>> Handle(GetCurrentEmployeeUnionMembershipQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await memberships.FindCurrentAsync(query.EmployeeId, asOf, cancellationToken);
        return current is null ? Error.NotFound(PayrollDomainErrors.UnionMembershipNotFound) : EmployeeUnionMembershipMapper.ToDto(current);
    }
}

[RequiresPermission("payroll.union.manage")]
public sealed record CreateEmployeeUnionMembershipCommand(Guid EmployeeId, EmployeeUnionMembershipData Data) : ICommand<Guid>;

public sealed class CreateEmployeeUnionMembershipValidator : AbstractValidator<CreateEmployeeUnionMembershipCommand>
{
    public CreateEmployeeUnionMembershipValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

/// <summary>Only one OPEN (EndDate == null) membership per employee at a time - a new membership after a prior one
/// ended (job change, re-joining) is fine, but two simultaneously-open memberships would make dues ambiguous.</summary>
public sealed class CreateEmployeeUnionMembershipHandler(ITenantContext tenant, IEmployeeUnionMembershipRepository memberships)
    : ICommandHandler<CreateEmployeeUnionMembershipCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEmployeeUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        if (command.Data.EndDate is null)
        {
            var existing = await memberships.ListByEmployeeAsync(command.EmployeeId, cancellationToken);
            if (existing.Any(m => m.IsOpen))
            {
                return Error.Conflict(PayrollDomainErrors.UnionMembershipAlreadyOpen);
            }
        }

        var created = EmployeeUnionMembership.Create(tenant.TenantId, command.EmployeeId, command.Data);
        if (created.IsFailure)
        {
            return created.Error;
        }

        memberships.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("payroll.union.manage")]
public sealed record UpdateEmployeeUnionMembershipCommand(Guid MembershipId, EmployeeUnionMembershipData Data) : ICommand;

public sealed class UpdateEmployeeUnionMembershipHandler(IEmployeeUnionMembershipRepository memberships) : ICommandHandler<UpdateEmployeeUnionMembershipCommand>
{
    public async Task<Result> Handle(UpdateEmployeeUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        var membership = await memberships.FindByIdAsync(command.MembershipId, cancellationToken);
        return membership is null ? Error.NotFound(PayrollDomainErrors.UnionMembershipNotFound) : membership.Update(command.Data);
    }
}

/// <summary>Ends coverage (employee left the union, or the TİS ended) as of <paramref name="EndDate"/>.</summary>
[RequiresPermission("payroll.union.manage")]
public sealed record EndEmployeeUnionMembershipCommand(Guid MembershipId, DateOnly EndDate) : ICommand;

public sealed class EndEmployeeUnionMembershipHandler(IEmployeeUnionMembershipRepository memberships) : ICommandHandler<EndEmployeeUnionMembershipCommand>
{
    public async Task<Result> Handle(EndEmployeeUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        var membership = await memberships.FindByIdAsync(command.MembershipId, cancellationToken);
        return membership is null ? Error.NotFound(PayrollDomainErrors.UnionMembershipNotFound) : membership.End(command.EndDate);
    }
}
