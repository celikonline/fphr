using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Application.Tasks;

public sealed record OnboardingBuddyAssignmentDto(Guid EmployeeId, Guid BuddyEmployeeId, DateOnly StartDate, DateOnly? EndDate)
{
    public static OnboardingBuddyAssignmentDto From(OnboardingBuddyAssignment assignment) =>
        new(assignment.EmployeeId, assignment.BuddyEmployeeId, assignment.StartDate, assignment.EndDate);
}

[RequiresPermission("onboarding.tasks.manage")]
public sealed record SaveOnboardingBuddyCommand(Guid EmployeeId, Guid BuddyEmployeeId, DateOnly StartDate, DateOnly? EndDate)
    : ICommand<OnboardingBuddyAssignmentDto>;

public sealed class SaveOnboardingBuddyValidator : AbstractValidator<SaveOnboardingBuddyCommand>
{
    public SaveOnboardingBuddyValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.BuddyEmployeeId).NotEmpty().NotEqual(x => x.EmployeeId);
        RuleFor(x => x.EndDate).Must((command, endDate) => endDate is null || endDate >= command.StartDate)
            .WithErrorCode("onboarding.buddy.invalid_date_range");
    }
}

public sealed class SaveOnboardingBuddyHandler(
    ITenantContext tenant,
    IEmployeeLookup employees,
    IOnboardingBuddyAssignmentRepository assignments)
    : ICommandHandler<SaveOnboardingBuddyCommand, OnboardingBuddyAssignmentDto>
{
    public async Task<Result<OnboardingBuddyAssignmentDto>> Handle(SaveOnboardingBuddyCommand command, CancellationToken cancellationToken)
    {
        if (!await employees.ExistsActiveAsync(tenant.TenantId, command.EmployeeId, cancellationToken) ||
            !await employees.ExistsActiveAsync(tenant.TenantId, command.BuddyEmployeeId, cancellationToken))
            return Error.NotFound("onboarding.buddy.employee_not_found");

        var existing = await assignments.GetByEmployeeIdAsync(command.EmployeeId, cancellationToken);
        if (existing is null)
        {
            var created = OnboardingBuddyAssignment.Create(tenant.TenantId, command.EmployeeId, command.BuddyEmployeeId,
                command.StartDate, command.EndDate);
            if (created.IsFailure) return created.Error;
            assignments.Add(created.Value);
            return OnboardingBuddyAssignmentDto.From(created.Value);
        }

        var updated = existing.Update(command.BuddyEmployeeId, command.StartDate, command.EndDate);
        return updated.IsFailure ? updated.Error : OnboardingBuddyAssignmentDto.From(existing);
    }
}

[RequiresPermission("onboarding.tasks.view")]
public sealed record GetOnboardingBuddyQuery(Guid EmployeeId) : IQuery<OnboardingBuddyAssignmentDto?>;

public sealed class GetOnboardingBuddyHandler(IOnboardingBuddyAssignmentRepository assignments)
    : IQueryHandler<GetOnboardingBuddyQuery, OnboardingBuddyAssignmentDto?>
{
    public async Task<Result<OnboardingBuddyAssignmentDto?>> Handle(GetOnboardingBuddyQuery query, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByEmployeeIdAsync(query.EmployeeId, cancellationToken);
        return Result.Success(assignment is null ? null : OnboardingBuddyAssignmentDto.From(assignment));
    }
}
