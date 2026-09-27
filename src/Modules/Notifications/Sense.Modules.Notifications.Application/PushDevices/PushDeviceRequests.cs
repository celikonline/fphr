using FluentValidation;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.PushDevices;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.PushDevices;

/// <summary>
/// Registers (or re-registers) the caller's Expo push token for this device - idempotent upsert (11.7,
/// push-bildirimleri-tasarim.md §2.1). Same token + same user + active row: touched, no new row. Same token,
/// different (active) user: the old row is revoked and a new one is added for the caller, so a device handed to a
/// different employee stops receiving the previous owner's pushes.
/// </summary>
[RequiresPermission("notifications.my.manage")]
public sealed record RegisterMyPushDeviceCommand(string Token, string Platform, string? DeviceName) : ICommand;

public sealed class RegisterMyPushDeviceValidator : AbstractValidator<RegisterMyPushDeviceCommand>
{
    public RegisterMyPushDeviceValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Platform).NotEmpty();
        RuleFor(x => x.DeviceName).MaximumLength(NotificationsLimits.DeviceNameMaxLength);
    }
}

public sealed class RegisterMyPushDeviceHandler(
    ITenantContext tenant, ICurrentUser currentUser, IPushDeviceTokenRepository devices, TimeProvider clock)
    : ICommandHandler<RegisterMyPushDeviceCommand>
{
    public async Task<Result> Handle(RegisterMyPushDeviceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > NotificationsLimits.PushTokenMaxLength)
        {
            return Error.Validation(NotificationsErrors.InvalidPushToken);
        }

        if (!PushPlatforms.All.Contains(command.Platform, StringComparer.Ordinal))
        {
            return Error.Validation(NotificationsErrors.InvalidPushPlatform);
        }

        if (currentUser.UserId is not { } userId)
        {
            return Result.Success();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await devices.FindActiveByTokenAsync(command.Token, cancellationToken);
        if (existing is not null && existing.UserId == userId)
        {
            existing.Touch(now);
            return Result.Success();
        }

        // A different (or no) active owner: the device changed hands, revoke the stale row before adding ours.
        existing?.Revoke(now);
        devices.Add(new PushDeviceToken(tenant.TenantId, userId, command.Token, command.Platform, command.DeviceName, now));
        return Result.Success();
    }
}

/// <summary>
/// Revokes the caller's own Expo push token, e.g. on logout (11.7, push-bildirimleri-tasarim.md §2.2). Idempotent
/// and safe to call for a token that does not exist or is not the caller's: no-op, still succeeds - the endpoint
/// never reveals whether the token was found or who owns it.
/// </summary>
[RequiresPermission("notifications.my.manage")]
public sealed record RevokeMyPushDeviceCommand(string Token) : ICommand;

public sealed class RevokeMyPushDeviceValidator : AbstractValidator<RevokeMyPushDeviceCommand>
{
    public RevokeMyPushDeviceValidator() => RuleFor(x => x.Token).NotEmpty();
}

public sealed class RevokeMyPushDeviceHandler(ICurrentUser currentUser, IPushDeviceTokenRepository devices, TimeProvider clock)
    : ICommandHandler<RevokeMyPushDeviceCommand>
{
    public async Task<Result> Handle(RevokeMyPushDeviceCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Success();
        }

        var existing = await devices.FindActiveByTokenAsync(command.Token, cancellationToken);
        if (existing is not null && existing.UserId == userId)
        {
            existing.Revoke(clock.GetUtcNow().UtcDateTime);
        }

        return Result.Success();
    }
}
