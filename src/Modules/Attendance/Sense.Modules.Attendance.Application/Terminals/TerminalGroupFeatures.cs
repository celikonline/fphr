using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

public sealed record TerminalGroupDto(Guid Id, string Name);

/// <summary>Terminal grupları (Z5.1).</summary>
[RequiresPermission("attendance.terminal.view")]
public sealed record ListTerminalGroupsQuery : IQuery<IReadOnlyList<TerminalGroupDto>>;

public sealed class ListTerminalGroupsHandler(ITerminalRepository terminals) : IQueryHandler<ListTerminalGroupsQuery, IReadOnlyList<TerminalGroupDto>>
{
    public async Task<Result<IReadOnlyList<TerminalGroupDto>>> Handle(ListTerminalGroupsQuery query, CancellationToken cancellationToken) =>
        (await terminals.ListGroupsAsync(cancellationToken)).Select(g => new TerminalGroupDto(g.Id, g.Name)).ToList();
}

[RequiresPermission("attendance.terminal.manage")]
public sealed record CreateTerminalGroupCommand(string Name) : ICommand<Guid>;

public sealed class CreateTerminalGroupHandler(ITerminalRepository terminals, ITenantContext tenant) : ICommandHandler<CreateTerminalGroupCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateTerminalGroupCommand command, CancellationToken cancellationToken)
    {
        var created = TerminalGroup.Create(tenant.TenantId, command.Name);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<Guid>>(created.Error);
        }

        terminals.AddGroup(created.Value);
        return Task.FromResult<Result<Guid>>(created.Value.Id);
    }
}

[RequiresPermission("attendance.terminal.manage")]
public sealed record RenameTerminalGroupCommand(Guid GroupId, string Name) : ICommand;

public sealed class RenameTerminalGroupHandler(ITerminalRepository terminals) : ICommandHandler<RenameTerminalGroupCommand>
{
    public async Task<Result> Handle(RenameTerminalGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await terminals.GetGroupByIdAsync(command.GroupId, cancellationToken);
        return group.IsFailure ? group.Error : group.Value.Rename(command.Name);
    }
}

/// <summary>Silme yalnız boş grupta: gruba bağlı terminal varsa <see cref="TerminalErrors.GroupInUse"/> (önce terminaller taşınır).</summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record DeleteTerminalGroupCommand(Guid GroupId) : ICommand;

public sealed class DeleteTerminalGroupHandler(ITerminalRepository terminals) : ICommandHandler<DeleteTerminalGroupCommand>
{
    public async Task<Result> Handle(DeleteTerminalGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await terminals.GetGroupByIdAsync(command.GroupId, cancellationToken);
        if (group.IsFailure)
        {
            return group.Error;
        }

        if (await terminals.AnyTerminalInGroupAsync(group.Value.Id, cancellationToken))
        {
            return Error.Conflict(TerminalErrors.GroupInUse);
        }

        terminals.RemoveGroup(group.Value);
        return Result.Success();
    }
}
