using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.HrMessages;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.HrMessages;

public sealed record MessageGroupDto(Guid Id, string Name, EmployeeSelection? Selection)
{
    public static MessageGroupDto From(MessageGroup g) => new(g.Id, g.Name, HrMessageSelection.Deserialize(g.SelectionJson));
}

[RequiresPermission("notifications.messagegroups.manage")]
public sealed record ListMessageGroupsQuery : IQuery<IReadOnlyList<MessageGroupDto>>;

public sealed class ListMessageGroupsHandler(IMessageGroupRepository groups) : IQueryHandler<ListMessageGroupsQuery, IReadOnlyList<MessageGroupDto>>
{
    public async Task<Result<IReadOnlyList<MessageGroupDto>>> Handle(ListMessageGroupsQuery query, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<MessageGroupDto>>((await groups.ListAsync(cancellationToken)).Select(MessageGroupDto.From).ToList());
}

[RequiresPermission("notifications.messagegroups.manage")]
public sealed record CreateMessageGroupCommand(string Name, EmployeeSelection Selection) : ICommand<Guid>;

[RequiresPermission("notifications.messagegroups.manage")]
public sealed record UpdateMessageGroupCommand(Guid Id, string Name, EmployeeSelection Selection) : ICommand;

[RequiresPermission("notifications.messagegroups.manage")]
public sealed record DeleteMessageGroupCommand(Guid Id) : ICommand;

public sealed class CreateMessageGroupValidator : AbstractValidator<CreateMessageGroupCommand>
{
    public CreateMessageGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(NotificationsLimits.MessageGroupNameMaxLength);
        RuleFor(x => x.Selection).Must(s => !HrMessageSelection.IsEmpty(s)).WithMessage("Selection is required.");
    }
}

public sealed class UpdateMessageGroupValidator : AbstractValidator<UpdateMessageGroupCommand>
{
    public UpdateMessageGroupValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(NotificationsLimits.MessageGroupNameMaxLength);
        RuleFor(x => x.Selection).Must(s => !HrMessageSelection.IsEmpty(s)).WithMessage("Selection is required.");
    }
}

public sealed class CreateMessageGroupHandler(ITenantContext tenant, IMessageGroupRepository groups)
    : ICommandHandler<CreateMessageGroupCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMessageGroupCommand command, CancellationToken cancellationToken)
    {
        if (await groups.NameExistsAsync(command.Name.Trim(), null, cancellationToken))
        {
            return Error.Conflict(NotificationsErrors.GroupNameTaken);
        }

        var group = new MessageGroup(tenant.TenantId, command.Name, HrMessageSelection.Serialize(command.Selection));
        groups.Add(group);
        return group.Id;
    }
}

public sealed class UpdateMessageGroupHandler(IMessageGroupRepository groups) : ICommandHandler<UpdateMessageGroupCommand>
{
    public async Task<Result> Handle(UpdateMessageGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(command.Id, cancellationToken);
        if (group is null)
        {
            return Error.NotFound(NotificationsErrors.GroupNotFound);
        }

        if (await groups.NameExistsAsync(command.Name.Trim(), command.Id, cancellationToken))
        {
            return Error.Conflict(NotificationsErrors.GroupNameTaken);
        }

        group.Update(command.Name, HrMessageSelection.Serialize(command.Selection));
        return Result.Success();
    }
}

public sealed class DeleteMessageGroupHandler(IMessageGroupRepository groups) : ICommandHandler<DeleteMessageGroupCommand>
{
    public async Task<Result> Handle(DeleteMessageGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(command.Id, cancellationToken);
        if (group is null)
        {
            return Error.NotFound(NotificationsErrors.GroupNotFound);
        }

        groups.Remove(group);
        return Result.Success();
    }
}
