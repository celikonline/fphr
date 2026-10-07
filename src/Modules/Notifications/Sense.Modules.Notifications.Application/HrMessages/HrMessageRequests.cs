using System.Text.Json;
using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.HrMessages;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.HrMessages;

public static class HrMessageChannels
{
    public const string Sms = "sms";

    public static IReadOnlyList<string> All { get; } = [NotificationChannels.Email, NotificationChannels.InApp, Sms];
}

public static class HrMessageSelection
{
    public const string ActionPath = "/app/messages";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(EmployeeSelection selection) => JsonSerializer.Serialize(selection, JsonOptions);

    public static EmployeeSelection? Deserialize(string json) => JsonSerializer.Deserialize<EmployeeSelection>(json, JsonOptions);

    /// <summary>True when the selection names no employees and has no filter (would otherwise mean "nobody" or, worse, "everybody").</summary>
    public static bool IsEmpty(EmployeeSelection? selection) =>
        selection is null || ((selection.EmployeeIds is null || selection.EmployeeIds.Count == 0) && selection.Filter is null);
}

public sealed record HrMessageDto(
    Guid Id, Guid SenderUserId, string Subject, string Body, string Importance, IReadOnlyList<string> Channels, int RecipientCount, DateTime SentAt,
    string? SenderName = null, IReadOnlyList<string>? RecipientPreview = null);

public sealed record HrMessagesPageDto(IReadOnlyList<HrMessageDto> Items, int Page, int PageSize, long TotalCount);

public sealed record SendHrMessageResult(Guid Id, int RecipientCount);

/// <summary>
/// Sends an HR message. Recipients = explicit/filter selection plus every group's selection, resolved now (snapshot).
/// An empty selection, or one that resolves to nobody, is rejected. SMS is rejected while no SMS provider is configured (K5).
/// </summary>
[RequiresPermission("notifications.messages.send")]
public sealed record SendHrMessageCommand(
    string Subject,
    string Body,
    HrMessageImportance Importance,
    IReadOnlyList<string> Channels,
    EmployeeSelection? Selection,
    IReadOnlyList<Guid>? GroupIds) : ICommand<SendHrMessageResult>;

public sealed class SendHrMessageValidator : AbstractValidator<SendHrMessageCommand>
{
    public SendHrMessageValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(NotificationsLimits.HrMessageSubjectMaxLength);
        RuleFor(x => x.Body)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(NotificationsLimits.HrMessageBodyMaxLength)
            .Must(body => HrMessageContentSanitizer.VisibleTextLength(body) <= NotificationsLimits.HrMessageVisibleTextMaxLength)
            .WithMessage($"The visible text cannot exceed {NotificationsLimits.HrMessageVisibleTextMaxLength} characters.");
        RuleFor(x => x.Channels).NotEmpty();
        RuleFor(x => x).Must(x => !HrMessageSelection.IsEmpty(x.Selection) || x.GroupIds is { Count: > 0 })
            .WithMessage("Recipients are required.").OverridePropertyName("Recipients");
    }
}

public sealed class SendHrMessageHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeDirectory directory,
    NotificationRecipientResolver recipientResolver,
    NotificationPublisher publisher,
    IMessageGroupRepository groups,
    IHrMessageRepository messages,
    ISmsSender smsSender)
    : ICommandHandler<SendHrMessageCommand, SendHrMessageResult>
{
    public async Task<Result<SendHrMessageResult>> Handle(SendHrMessageCommand command, CancellationToken cancellationToken)
    {
        var channels = command.Channels.Distinct(StringComparer.Ordinal).ToList();
        if (channels.Exists(c => !HrMessageChannels.All.Contains(c, StringComparer.Ordinal)))
        {
            return Error.Validation(NotificationsErrors.UnknownMessageChannel);
        }

        if (channels.Contains(HrMessageChannels.Sms, StringComparer.Ordinal) && !smsSender.IsAvailable)
        {
            return Error.Failure(NotificationsErrors.SmsChannelUnavailable);
        }

        if (currentUser.UserId is not { } senderUserId)
        {
            return Error.Unauthorized(NotificationsErrors.RecipientsRequired);
        }

        var selections = new List<EmployeeSelection>();
        if (!HrMessageSelection.IsEmpty(command.Selection))
        {
            selections.Add(command.Selection!);
        }

        var groupIds = (command.GroupIds ?? []).Distinct().ToList();
        if (groupIds.Count > 0)
        {
            var found = await groups.ListByIdsAsync(groupIds, cancellationToken);
            if (found.Count != groupIds.Count)
            {
                return Error.NotFound(NotificationsErrors.GroupNotFound);
            }

            // Resolved now, not when the group was saved: membership changes are picked up (snapshot at send time).
            selections.AddRange(found.Select(g => HrMessageSelection.Deserialize(g.SelectionJson)).OfType<EmployeeSelection>());
        }

        if (selections.Count == 0)
        {
            return Error.Validation(NotificationsErrors.RecipientsRequired);
        }

        var employeeIds = new HashSet<Guid>();
        foreach (var selection in selections)
        {
            employeeIds.UnionWith(await directory.ResolveSelectionAsync(tenant.TenantId, selection, cancellationToken));
        }

        var recipients = await recipientResolver.ResolveAsync(tenant.TenantId, employeeIds, cancellationToken);
        if (recipients.Count == 0)
        {
            return Error.Rule(NotificationsErrors.RecipientsRequired);
        }

        var sanitizedBody = HrMessageContentSanitizer.Sanitize(command.Body);
        var message = new HrMessage(tenant.TenantId, senderUserId, command.Subject, sanitizedBody, command.Importance, channels);
        foreach (var recipient in recipients)
        {
            message.AddRecipient(recipient.EmployeeId, recipient.UserId);
        }

        messages.Add(message);

        var model = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["message_subject"] = message.Subject,
            ["message_body"] = message.Body,
            ["importance"] = message.IsHighPriority ? "high" : "normal",
            ["is_high_priority"] = message.IsHighPriority ? "true" : "false",
        };

        // Saves the message together with the notification rows.
        await publisher.PublishAsync(
            tenant.TenantId,
            new NotificationRequest(message.Id, NotificationTemplateKeys.HrMessage, recipients, model, HrMessageSelection.ActionPath, channels),
            cancellationToken);

        return new SendHrMessageResult(message.Id, recipients.Count);
    }
}

/// <summary>Messages the caller received (in-app inbox).</summary>
[RequiresPermission("notifications.messages.view")]
public sealed record ListMyInboxQuery(int Page = PagingDefaults.FirstPage, int PageSize = PagingDefaults.DefaultPageSize) : IQuery<HrMessagesPageDto>;

/// <summary>Messages the caller sent.</summary>
[RequiresPermission("notifications.messages.view")]
public sealed record ListMyOutboxQuery(int Page = PagingDefaults.FirstPage, int PageSize = PagingDefaults.DefaultPageSize) : IQuery<HrMessagesPageDto>;

public sealed class ListMyInboxValidator : AbstractValidator<ListMyInboxQuery>
{
    public ListMyInboxValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(PagingDefaults.FirstPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingDefaults.MaxPageSize);
    }
}

public sealed class ListMyOutboxValidator : AbstractValidator<ListMyOutboxQuery>
{
    public ListMyOutboxValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(PagingDefaults.FirstPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingDefaults.MaxPageSize);
    }
}

/// <summary>Builds list DTOs with sender display name and a short recipient preview using batched lookups (no N+1).</summary>
public sealed class HrMessageDtoBuilder(
    ITenantContext tenant, IHrMessageRepository messages, IEmployeeContactLookup contacts, IUserProfileLookup profiles)
{
    public const int PreviewSize = 3;

    public async Task<IReadOnlyList<HrMessageDto>> BuildAsync(IReadOnlyList<HrMessage> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var messageIds = items.Select(m => m.Id).ToList();
        var counts = await messages.CountRecipientsAsync(messageIds, cancellationToken);
        var previews = await messages.GetRecipientPreviewsAsync(messageIds, PreviewSize, cancellationToken);
        var senders = await profiles.GetAsync(tenant.TenantId, items.Select(m => m.SenderUserId).Distinct().ToList(), cancellationToken);
        var employeeIds = previews.Values.SelectMany(v => v).Distinct().ToList();
        var names = employeeIds.Count == 0
            ? []
            : (await contacts.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken))
                .ToDictionary(c => c.EmployeeId, c => ShortName(c.FirstName, c.LastName));

        return items.Select(m => new HrMessageDto(
            m.Id, m.SenderUserId, m.Subject, m.Body, m.IsHighPriority ? "high" : "normal",
            m.Channels.Split(',', StringSplitOptions.RemoveEmptyEntries), counts.GetValueOrDefault(m.Id), m.CreatedAt,
            senders.TryGetValue(m.SenderUserId, out var sender) ? sender.DisplayName : null,
            previews.GetValueOrDefault(m.Id, []).Where(names.ContainsKey).Select(id => names[id]).ToList())).ToList();
    }

    /// <summary>"Ergün Erkan" becomes "Ergün E."</summary>
    internal static string ShortName(string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName.Trim() : $"{firstName.Trim()} {lastName.Trim()[0]}.";
}

public sealed class ListMyInboxHandler(ICurrentUser currentUser, IHrMessageRepository messages, HrMessageDtoBuilder dtoBuilder)
    : IQueryHandler<ListMyInboxQuery, HrMessagesPageDto>
{
    public async Task<Result<HrMessagesPageDto>> Handle(ListMyInboxQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new HrMessagesPageDto([], query.Page, query.PageSize, 0);
        }

        var (items, total) = await messages.ListInboxAsync(userId, query.Page, query.PageSize, cancellationToken);
        return new HrMessagesPageDto(await dtoBuilder.BuildAsync(items, cancellationToken), query.Page, query.PageSize, total);
    }
}

public sealed class ListMyOutboxHandler(ICurrentUser currentUser, IHrMessageRepository messages, HrMessageDtoBuilder dtoBuilder)
    : IQueryHandler<ListMyOutboxQuery, HrMessagesPageDto>
{
    public async Task<Result<HrMessagesPageDto>> Handle(ListMyOutboxQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new HrMessagesPageDto([], query.Page, query.PageSize, 0);
        }

        var (items, total) = await messages.ListSentAsync(userId, query.Page, query.PageSize, cancellationToken);
        return new HrMessagesPageDto(await dtoBuilder.BuildAsync(items, cancellationToken), query.Page, query.PageSize, total);
    }
}
