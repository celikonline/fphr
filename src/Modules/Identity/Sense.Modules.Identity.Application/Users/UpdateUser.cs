using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record UpdateUserCommand(Guid UserId, string FirstName, string LastName, string? Phone, string? Language, Guid? EmployeeId) : ICommand;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
    }
}

public sealed class UpdateUserHandler(IUserRepository users) : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        user.UpdateProfile(command.FirstName, command.LastName, command.Phone, command.Language);
        if (command.EmployeeId is { } employeeId)
        {
            user.LinkEmployee(employeeId);
        }
        else
        {
            user.UnlinkEmployee();
        }

        return Result.Success();
    }
}
