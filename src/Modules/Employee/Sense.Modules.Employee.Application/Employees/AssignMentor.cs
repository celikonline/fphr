using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Assigns (or clears, with null) a mentor for onboarding ("mentör atama", Faz 4 #72) - independent of the manager hierarchy.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record AssignMentorCommand(Guid EmployeeId, Guid? MentorEmployeeId) : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class AssignMentorValidator : AbstractValidator<AssignMentorCommand>
{
    public AssignMentorValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}

public sealed class AssignMentorHandler(IEmployeeRepository employees) : ICommandHandler<AssignMentorCommand>
{
    public async Task<Result> Handle(AssignMentorCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (command.MentorEmployeeId is { } mentorId && !await employees.ExistsActiveAsync(mentorId, cancellationToken))
        {
            return Error.NotFound(EmployeeErrors.MentorNotFound);
        }

        return found.Value.AssignMentor(command.MentorEmployeeId);
    }
}
