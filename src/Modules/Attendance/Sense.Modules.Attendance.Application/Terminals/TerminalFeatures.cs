using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// Terminal listesi/ayrıntısı: ham anahtar ya da özeti ASLA dönmez, yalnız tanıma amaçlı önek. <see cref="LastSuspiciousAtUtc"/>: son "şüpheli
/// deneme" (PIN başarısızlık bütçesi doldu) - bellek içi, süreç yeniden başlayınca sıfırlanır.
/// </summary>
public sealed record TerminalDto(
    Guid Id, string Code, string Name, Guid? TerminalGroupId, Guid? LocationId, TerminalDirectionMode DirectionMode, string ApiKeyPrefix,
    DateTime? LastSeenAtUtc, bool IsOnline, bool IsActive, DateTime? LastSuspiciousAtUtc = null);

public sealed record TerminalInput(string Code, string Name, Guid? TerminalGroupId, Guid? LocationId, TerminalDirectionMode DirectionMode);

/// <summary>Oluşturma/anahtar yenileme yanıtı: ham anahtar yalnız burada, bir kez. <see cref="ToString"/> anahtarı yazmaz.</summary>
public sealed record TerminalApiKeyDto(Guid TerminalId, string ApiKey, string ApiKeyPrefix)
{
    public override string ToString() => $"{nameof(TerminalApiKeyDto)} {{ {nameof(TerminalId)} = {TerminalId}, {nameof(ApiKeyPrefix)} = {ApiKeyPrefix} }}";
}

internal static class TerminalMapper
{
    /// <summary>Son görülme bu süreden eskiyse terminal çevrimdışı sayılır (Z5.1/Z5.3).</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    public static TerminalDto ToDto(Terminal t, DateTime nowUtc, TerminalCredentialFailureMonitor pinFailures) =>
        new(t.Id, t.Code, t.Name, t.TerminalGroupId, t.LocationId, t.DirectionMode, t.ApiKeyPrefix, t.LastSeenAtUtc,
            IsOnline(t.LastSeenAtUtc, nowUtc), t.IsActive, pinFailures.LastSuspiciousAtUtc(t.Id));

    public static bool IsOnline(DateTime? lastSeenAtUtc, DateTime nowUtc) => lastSeenAtUtc is { } seen && nowUtc - seen < OnlineWindow;

    /// <summary>Kod tekilliği, grup ve lokasyonun kiracıda varlığı.</summary>
    public static async Task<Error?> CheckReferencesAsync(
        ITerminalRepository terminals, ILocationLookup locations, Guid tenantId, TerminalInput input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.Code) && await terminals.TerminalCodeExistsAsync(input.Code, excludeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(TerminalErrors.CodeDuplicate);
        }

        if (input.TerminalGroupId is { } groupId)
        {
            var group = await terminals.GetGroupByIdAsync(groupId, cancellationToken).ConfigureAwait(false);
            if (group.IsFailure)
            {
                return group.Error;
            }
        }

        if (input.LocationId is { } locationId && !await locations.ExistsActiveAsync(tenantId, locationId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound(TerminalErrors.LocationNotFound);
        }

        return null;
    }
}

[RequiresPermission("attendance.terminal.view")]
public sealed record ListTerminalsQuery : IQuery<IReadOnlyList<TerminalDto>>;

public sealed class ListTerminalsHandler(ITerminalRepository terminals, TerminalCredentialFailureMonitor pinFailures, TimeProvider clock)
    : IQueryHandler<ListTerminalsQuery, IReadOnlyList<TerminalDto>>
{
    public async Task<Result<IReadOnlyList<TerminalDto>>> Handle(ListTerminalsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return (await terminals.ListTerminalsAsync(cancellationToken)).Select(t => TerminalMapper.ToDto(t, now, pinFailures)).ToList();
    }
}

[RequiresPermission("attendance.terminal.view")]
public sealed record GetTerminalQuery(Guid TerminalId) : IQuery<TerminalDto>;

public sealed class GetTerminalHandler(ITerminalRepository terminals, TerminalCredentialFailureMonitor pinFailures, TimeProvider clock)
    : IQueryHandler<GetTerminalQuery, TerminalDto>
{
    public async Task<Result<TerminalDto>> Handle(GetTerminalQuery query, CancellationToken cancellationToken)
    {
        var terminal = await terminals.GetTerminalByIdAsync(query.TerminalId, cancellationToken);
        return terminal.IsFailure ? terminal.Error : TerminalMapper.ToDto(terminal.Value, clock.GetUtcNow().UtcDateTime, pinFailures);
    }
}

/// <summary>Yeni terminal + ilk API anahtarı (ham anahtar yanıtta bir kez; yalnız SHA-256 özeti ve önek saklanır).</summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record CreateTerminalCommand(TerminalInput Terminal) : ICommand<TerminalApiKeyDto>;

public sealed class CreateTerminalHandler(ITerminalRepository terminals, ILocationLookup locations, ITenantContext tenant)
    : ICommandHandler<CreateTerminalCommand, TerminalApiKeyDto>
{
    public async Task<Result<TerminalApiKeyDto>> Handle(CreateTerminalCommand command, CancellationToken cancellationToken)
    {
        var input = command.Terminal;
        if (await TerminalMapper.CheckReferencesAsync(terminals, locations, tenant.TenantId, input, null, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var key = TerminalApiKey.Generate();
        var created = Terminal.Create(tenant.TenantId, input.Code, input.Name, input.TerminalGroupId, input.LocationId, input.DirectionMode, key.Hash, key.Prefix);
        if (created.IsFailure)
        {
            return created.Error;
        }

        terminals.AddTerminal(created.Value);
        return new TerminalApiKeyDto(created.Value.Id, key.Key, key.Prefix);
    }
}

[RequiresPermission("attendance.terminal.manage")]
public sealed record UpdateTerminalCommand(Guid TerminalId, TerminalInput Terminal) : ICommand;

public sealed class UpdateTerminalHandler(ITerminalRepository terminals, ILocationLookup locations, ITenantContext tenant) : ICommandHandler<UpdateTerminalCommand>
{
    public async Task<Result> Handle(UpdateTerminalCommand command, CancellationToken cancellationToken)
    {
        var terminal = await terminals.GetTerminalByIdAsync(command.TerminalId, cancellationToken);
        if (terminal.IsFailure)
        {
            return terminal.Error;
        }

        var input = command.Terminal;
        if (await TerminalMapper.CheckReferencesAsync(terminals, locations, tenant.TenantId, input, terminal.Value.Id, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        return terminal.Value.Update(input.Code, input.Name, input.TerminalGroupId, input.LocationId, input.DirectionMode);
    }
}

/// <summary>Anahtar yenileme: yeni anahtar yanıtta bir kez döner, eski anahtar aynı işlemde geçersizleşir (özeti üzerine yazılır).</summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record RotateTerminalApiKeyCommand(Guid TerminalId) : ICommand<TerminalApiKeyDto>;

public sealed class RotateTerminalApiKeyHandler(ITerminalRepository terminals) : ICommandHandler<RotateTerminalApiKeyCommand, TerminalApiKeyDto>
{
    public async Task<Result<TerminalApiKeyDto>> Handle(RotateTerminalApiKeyCommand command, CancellationToken cancellationToken)
    {
        var terminal = await terminals.GetTerminalByIdAsync(command.TerminalId, cancellationToken);
        if (terminal.IsFailure)
        {
            return terminal.Error;
        }

        var key = TerminalApiKey.Generate();
        var rotated = terminal.Value.RotateApiKey(key.Hash, key.Prefix);
        return rotated.IsFailure ? rotated.Error : new TerminalApiKeyDto(terminal.Value.Id, key.Key, key.Prefix);
    }
}

/// <summary>Pasife alma (silme yok: okutmalar TerminalId'ye bağlı kalır). Pasif terminalin anahtarı kimlik doğrulamada reddedilir.</summary>
[RequiresPermission("attendance.terminal.manage")]
public sealed record SetTerminalActiveCommand(Guid TerminalId, bool IsActive) : ICommand;

public sealed class SetTerminalActiveHandler(ITerminalRepository terminals) : ICommandHandler<SetTerminalActiveCommand>
{
    public async Task<Result> Handle(SetTerminalActiveCommand command, CancellationToken cancellationToken)
    {
        var terminal = await terminals.GetTerminalByIdAsync(command.TerminalId, cancellationToken);
        if (terminal.IsFailure)
        {
            return terminal.Error;
        }

        if (command.IsActive)
        {
            terminal.Value.Activate();
        }
        else
        {
            terminal.Value.Deactivate();
        }

        return Result.Success();
    }
}
