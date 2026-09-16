using FluentValidation;
using Sense.Modules.Integrations.Contracts;
using Sense.Modules.Integrations.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Integrations.Application.Webhooks;

/// <summary>Registers a webhook subscription. The response carries the generated secret exactly once.</summary>
[RequiresPermission(IntegrationsPermissions.WebhooksManageKey)]
public sealed record RegisterWebhookSubscriptionCommand(string Url, IReadOnlyList<string> EventTypes) : ICommand<WebhookSubscriptionCreatedDto>;

public sealed class RegisterWebhookSubscriptionValidator : AbstractValidator<RegisterWebhookSubscriptionCommand>
{
    public RegisterWebhookSubscriptionValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(IntegrationsLimits.UrlMaxLength);
        RuleFor(x => x.EventTypes).NotEmpty();
    }
}

public sealed class RegisterWebhookSubscriptionHandler(ITenantContext tenant, IWebhookSubscriptionRepository subscriptions)
    : ICommandHandler<RegisterWebhookSubscriptionCommand, WebhookSubscriptionCreatedDto>
{
    public Task<Result<WebhookSubscriptionCreatedDto>> Handle(RegisterWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var created = Domain.Webhooks.WebhookSubscription.Create(tenant.TenantId, command.Url, command.EventTypes);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<WebhookSubscriptionCreatedDto>>(created.Error);
        }

        subscriptions.Add(created.Value);
        return Task.FromResult<Result<WebhookSubscriptionCreatedDto>>(new WebhookSubscriptionCreatedDto(created.Value.Id, created.Value.Secret));
    }
}

/// <summary>Stops deliveries to a subscription without deleting its history.</summary>
[RequiresPermission(IntegrationsPermissions.WebhooksManageKey)]
public sealed record DeactivateWebhookSubscriptionCommand(Guid Id) : ICommand;

public sealed class DeactivateWebhookSubscriptionHandler(IWebhookSubscriptionRepository subscriptions) : ICommandHandler<DeactivateWebhookSubscriptionCommand>
{
    public async Task<Result> Handle(DeactivateWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var existing = await subscriptions.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.Deactivate();
        return Result.Success();
    }
}

/// <summary>Resumes deliveries to a previously deactivated subscription.</summary>
[RequiresPermission(IntegrationsPermissions.WebhooksManageKey)]
public sealed record ReactivateWebhookSubscriptionCommand(Guid Id) : ICommand;

public sealed class ReactivateWebhookSubscriptionHandler(IWebhookSubscriptionRepository subscriptions) : ICommandHandler<ReactivateWebhookSubscriptionCommand>
{
    public async Task<Result> Handle(ReactivateWebhookSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var existing = await subscriptions.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.Reactivate();
        return Result.Success();
    }
}
