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

/// <summary>Kullanıcı tanımlama: kayıt oluşturulur (Invited), roller atanır, istenirse davet e-postası gönderilir.</summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record CreateUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    IReadOnlyList<Guid> RoleIds,
    Guid? EmployeeId,
    bool SendInvitation,
    string? Language) : ICommand<Guid>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.RoleIds).NotNull();
    }
}

public sealed class CreateUserHandler(
    IUserRepository users,
    IRoleRepository roles,
    ITenantContext tenant,
    IDispatcher dispatcher,
    AccessTypeGuard accessTypeGuard,
    IOptions<IdentityOptions> options) : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (await users.EmailExistsAsync(command.Email, null, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(IdentityErrors.UserEmailTaken);
        }

        var roleEntities = await roles.GetByIdsAsync(command.RoleIds, cancellationToken).ConfigureAwait(false);
        if (roleEntities.Count != command.RoleIds.Distinct().Count())
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var allowed = await accessTypeGuard.EnsureCanChangeAsync([], command.RoleIds, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var created = User.Create(tenant.TenantId, command.Email, command.FirstName, command.LastName,
            string.IsNullOrWhiteSpace(command.Language) ? options.Value.DefaultLanguage : command.Language, command.Phone);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var user = created.Value;
        user.SetRoles(command.RoleIds);
        if (command.EmployeeId is { } employeeId)
        {
            user.LinkEmployee(employeeId);
        }

        users.Add(user);

        if (command.SendInvitation)
        {
            var invite = await dispatcher.Send(new SendInvitationCommand(user.Id), cancellationToken).ConfigureAwait(false);
            if (invite.IsFailure)
            {
                return invite.Error;
            }
        }

        return user.Id;
    }
}
