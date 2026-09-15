using FluentValidation;
using Sense.Modules.Notifications.Application.Catalog;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.Preferences;

public sealed record NotificationChannelSettingDto(string Channel, bool Enabled, bool Locked);

/// <summary>One catalog row on "E-posta bildirim ayarları": display names come from the web resource "notifications:template.{key}".</summary>
public sealed record NotificationPreferenceDto(string TemplateKey, NotificationCategory Category, bool IsMandatory, IReadOnlyList<NotificationChannelSettingDto> Channels);

public sealed record NotificationPreferenceInput(string TemplateKey, string Channel, bool Enabled);

/// <summary>The catalog merged with the caller's explicit choices.</summary>
[RequiresPermission("notifications.my.view")]
public sealed record GetMyNotificationPreferencesQuery : IQuery<IReadOnlyList<NotificationPreferenceDto>>;

public sealed class GetMyNotificationPreferencesHandler(ICurrentUser currentUser, INotificationPreferenceRepository preferences)
    : IQueryHandler<GetMyNotificationPreferencesQuery, IReadOnlyList<NotificationPreferenceDto>>
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> Handle(GetMyNotificationPreferencesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationPreference> mine = currentUser.UserId is { } userId
            ? await preferences.ListForUserAsync(userId, cancellationToken)
            : [];

        IReadOnlyList<NotificationPreferenceDto> result = NotificationCatalog.All
            .Select(d => new NotificationPreferenceDto(
                d.Key,
                d.Category,
                d.IsMandatory,
                d.DefaultChannels
                    .Select(channel => new NotificationChannelSettingDto(
                        channel,
                        NotificationCatalog.IsEnabled(d, channel, mine.FirstOrDefault(p => p.TemplateKey == d.Key && p.Channel == channel)?.Enabled),
                        d.IsMandatory))
                    .ToList()))
            .ToList();

        return Result.Success(result);
    }
}

/// <summary>Upserts the caller's on/off choices; mandatory templates and channels a template does not use are rejected.</summary>
[RequiresPermission("notifications.my.manage")]
public sealed record UpdateMyNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput> Items) : ICommand;

public sealed class UpdateMyNotificationPreferencesValidator : AbstractValidator<UpdateMyNotificationPreferencesCommand>
{
    private const int MaxItems = 200;

    public UpdateMyNotificationPreferencesValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= MaxItems);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.TemplateKey).NotEmpty().MaximumLength(NotificationsLimits.TemplateKeyMaxLength);
            item.RuleFor(i => i.Channel).NotEmpty().MaximumLength(NotificationsLimits.ChannelMaxLength);
        });
    }
}

public sealed class UpdateMyNotificationPreferencesHandler(
    ITenantContext tenant, ICurrentUser currentUser, INotificationPreferenceRepository preferences)
    : ICommandHandler<UpdateMyNotificationPreferencesCommand>
{
    public async Task<Result> Handle(UpdateMyNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        foreach (var item in command.Items)
        {
            var definition = NotificationCatalog.Find(item.TemplateKey);
            if (definition is null)
            {
                return Error.Validation(NotificationsErrors.UnknownTemplate, ("TemplateKey", item.TemplateKey));
            }

            if (!NotificationChannels.All.Contains(item.Channel, StringComparer.Ordinal) || !definition.SupportsChannel(item.Channel))
            {
                return Error.Validation(NotificationsErrors.UnknownChannel, ("Channel", item.Channel));
            }

            if (!definition.IsUserConfigurable)
            {
                return Error.Rule(NotificationsErrors.TemplateNotConfigurable, ("TemplateKey", item.TemplateKey));
            }
        }

        if (currentUser.UserId is not { } userId)
        {
            return Result.Success();
        }

        var existing = await preferences.ListForUserAsync(userId, cancellationToken);
        foreach (var item in command.Items)
        {
            var row = existing.FirstOrDefault(p => p.TemplateKey == item.TemplateKey && p.Channel == item.Channel);
            if (row is null)
            {
                preferences.Add(new NotificationPreference(tenant.TenantId, userId, item.TemplateKey, item.Channel, item.Enabled));
            }
            else
            {
                row.SetEnabled(item.Enabled);
            }
        }

        return Result.Success();
    }
}
