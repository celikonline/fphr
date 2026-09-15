using FluentValidation;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.Preferences;

/// <summary>Effective preference for one catalog template (override row, or the catalog default).</summary>
public sealed record NotificationPreferenceDto(string TemplateKey, string Category, bool EmailEnabled, bool InAppEnabled, bool IsMandatory);

public sealed record NotificationPreferenceInput(string TemplateKey, bool EmailEnabled, bool InAppEnabled);

[RequiresPermission(NotificationsPermissions.SelfPreferencesKey)]
public sealed record GetMyNotificationPreferencesQuery : IQuery<IReadOnlyList<NotificationPreferenceDto>>;

public sealed class GetMyNotificationPreferencesHandler(ICurrentUser currentUser, INotificationPreferenceRepository preferences)
    : IQueryHandler<GetMyNotificationPreferencesQuery, IReadOnlyList<NotificationPreferenceDto>>
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> Handle(GetMyNotificationPreferencesQuery query, CancellationToken cancellationToken)
    {
        var rows = currentUser.UserId is { } userId
            ? await preferences.ListForUserAsync(userId, cancellationToken)
            : [];
        var byKey = rows.ToDictionary(r => r.TemplateKey, StringComparer.Ordinal);

        IReadOnlyList<NotificationPreferenceDto> result = NotificationTemplateCatalog.All
            .Select(t =>
            {
                var row = byKey.GetValueOrDefault(t.Key);
                return new NotificationPreferenceDto(
                    t.Key,
                    System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(t.Category.ToString()),
                    t.IsMandatory || (row?.EmailEnabled ?? t.DefaultChannels.HasFlag(NotificationChannels.Email)),
                    t.IsMandatory || (row?.InAppEnabled ?? t.DefaultChannels.HasFlag(NotificationChannels.InApp)),
                    t.IsMandatory);
            })
            .ToList();

        return Result.Success(result);
    }
}

/// <summary>Upserts the caller's channel choices for the given catalog templates (unlisted templates keep their state).</summary>
[RequiresPermission(NotificationsPermissions.SelfPreferencesKey)]
public sealed record UpdateMyNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput> Preferences) : ICommand;

public sealed class UpdateMyNotificationPreferencesValidator : AbstractValidator<UpdateMyNotificationPreferencesCommand>
{
    public UpdateMyNotificationPreferencesValidator()
    {
        RuleFor(x => x.Preferences).NotNull();
        RuleForEach(x => x.Preferences).ChildRules(p =>
            p.RuleFor(x => x.TemplateKey).NotEmpty().MaximumLength(NotificationLimits.TemplateKeyMaxLength));
    }
}

public sealed class UpdateMyNotificationPreferencesHandler(ICurrentUser currentUser, ITenantContext tenant, INotificationPreferenceRepository preferences)
    : ICommandHandler<UpdateMyNotificationPreferencesCommand>
{
    public async Task<Result> Handle(UpdateMyNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        foreach (var input in command.Preferences)
        {
            var definition = NotificationTemplateCatalog.Find(input.TemplateKey);
            if (definition is null)
            {
                return Error.Validation(NotificationsErrors.UnknownTemplate, ("TemplateKey", input.TemplateKey));
            }

            if (definition.IsMandatory && (!input.EmailEnabled || !input.InAppEnabled))
            {
                return Error.Rule(NotificationsErrors.TemplateNotConfigurable, ("TemplateKey", input.TemplateKey));
            }
        }

        var existing = (await preferences.ListForUserAsync(userId, cancellationToken)).ToDictionary(r => r.TemplateKey, StringComparer.Ordinal);
        foreach (var input in command.Preferences)
        {
            if (existing.TryGetValue(input.TemplateKey, out var row))
            {
                row.Update(input.EmailEnabled, input.InAppEnabled);
                continue;
            }

            var created = NotificationPreference.Create(tenant.TenantId, userId, input.TemplateKey, input.EmailEnabled, input.InAppEnabled);
            if (created.IsFailure)
            {
                return created.Error;
            }

            preferences.Add(created.Value);
            existing[input.TemplateKey] = created.Value;
        }

        return Result.Success();
    }
}
