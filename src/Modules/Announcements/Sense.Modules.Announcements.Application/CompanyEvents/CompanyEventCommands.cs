using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>Manuel şirket etkinliği oluştur ("Etkinlik Oluştur" akışı, senseik-027 / roadmap #71-P-19).</summary>
[RequiresPermission("announcements.events.manage")]
public sealed record CreateCompanyEventCommand(
    string Title,
    string? Description,
    DateOnly EventDate,
    CompanyEventType Type,
    Guid? VisibilityOrgUnitId,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    bool? IsAllDay = null,
    CompanyEventMode? Mode = null,
    string? Location = null,
    string? OnlineUrl = null,
    bool NotifyInvitees = false,
    Guid? CategoryId = null,
    IReadOnlyList<Guid>? InviteeEmployeeIds = null) : ICommand<Guid>;

public sealed class CreateCompanyEventValidator : AbstractValidator<CreateCompanyEventCommand>
{
    public CreateCompanyEventValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CompanyEventLimits.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(CompanyEventLimits.DescriptionMaxLength);
        RuleFor(x => x.Type).IsInEnum().Must(t => t is CompanyEventType.CompanyEvent or CompanyEventType.Meeting)
            .WithMessage("Yalnızca CompanyEvent ve Meeting türleri manuel oluşturulabilir.");
        RuleFor(x => x.InviteeEmployeeIds).Must(ids => ids is null || ids.Count <= CompanyEventLimits.MaxInvitees);
    }
}

public sealed class CreateCompanyEventHandler(
    ITenantContext tenant, ICurrentUser currentUser, ICompanyEventRepository events, IOrgUnitLookup orgUnits,
    ICompanyEventCategoryRepository categories, IEmployeeContactLookup employeeContacts, IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateCompanyEventCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCompanyEventCommand command, CancellationToken cancellationToken)
    {
        if (command.VisibilityOrgUnitId is { } orgUnitId
            && !await orgUnits.ExistsActiveAsync(tenant.TenantId, orgUnitId, cancellationToken))
        {
            return Error.Validation(AnnouncementsErrors.VisibilityOrgUnitNotFound);
        }

        var category = await EventInvitationFeatures.ValidateCategoryAsync(categories, tenant.TenantId, command.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var invitees = await EventInvitationFeatures.ValidateInviteesAsync(employeeContacts, tenant.TenantId, command.InviteeEmployeeIds, cancellationToken);
        if (invitees.IsFailure)
        {
            return invitees.Error;
        }

        var plan = EventInvitationFeatures.BuildPlan(
            command.EventDate, command.StartsAt, command.EndsAt, command.IsAllDay, command.Mode, command.Location,
            command.OnlineUrl, command.NotifyInvitees, command.CategoryId);
        var created = CompanyEvent.Create(
            tenant.TenantId, command.Title, command.Description, command.Type,
            command.VisibilityOrgUnitId, currentUser.UserId ?? Guid.Empty, plan);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var added = created.Value.SetInvitees(invitees.Value);
        events.Add(created.Value);
        EventInvitationFeatures.NotifyIfRequested(outbox, created.Value, added);
        return created.Value.Id;
    }
}

/// <summary>Manuel şirket etkinliğini düzenle (senseik-027 / roadmap #71 - sil-yeniden oluşturma yerine).</summary>
[RequiresPermission("announcements.events.manage")]
public sealed record UpdateCompanyEventCommand(
    Guid EventId,
    string Title,
    string? Description,
    DateOnly EventDate,
    CompanyEventType Type,
    Guid? VisibilityOrgUnitId,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    bool? IsAllDay = null,
    CompanyEventMode? Mode = null,
    string? Location = null,
    string? OnlineUrl = null,
    bool NotifyInvitees = false,
    Guid? CategoryId = null,
    IReadOnlyList<Guid>? InviteeEmployeeIds = null) : ICommand;

public sealed class UpdateCompanyEventValidator : AbstractValidator<UpdateCompanyEventCommand>
{
    public UpdateCompanyEventValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CompanyEventLimits.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(CompanyEventLimits.DescriptionMaxLength);
        RuleFor(x => x.Type).IsInEnum().Must(t => t is CompanyEventType.CompanyEvent or CompanyEventType.Meeting)
            .WithMessage("Yalnızca CompanyEvent ve Meeting türleri manuel düzenlenebilir.");
        RuleFor(x => x.InviteeEmployeeIds).Must(ids => ids is null || ids.Count <= CompanyEventLimits.MaxInvitees);
    }
}

public sealed class UpdateCompanyEventHandler(
    ITenantContext tenant, ICompanyEventRepository events, IOrgUnitLookup orgUnits,
    ICompanyEventCategoryRepository categories, IEmployeeContactLookup employeeContacts, IIntegrationEventOutbox outbox)
    : ICommandHandler<UpdateCompanyEventCommand>
{
    public async Task<Result> Handle(UpdateCompanyEventCommand command, CancellationToken cancellationToken)
    {
        var companyEvent = await events.GetByIdAsync(tenant.TenantId, command.EventId, cancellationToken);
        if (companyEvent is null)
        {
            return Error.NotFound(CompanyEventDomainErrors.NotFound);
        }

        if (command.VisibilityOrgUnitId is { } orgUnitId
            && !await orgUnits.ExistsActiveAsync(tenant.TenantId, orgUnitId, cancellationToken))
        {
            return Error.Validation(AnnouncementsErrors.VisibilityOrgUnitNotFound);
        }

        var category = await EventInvitationFeatures.ValidateCategoryAsync(categories, tenant.TenantId, command.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category;
        }

        var invitees = await EventInvitationFeatures.ValidateInviteesAsync(employeeContacts, tenant.TenantId, command.InviteeEmployeeIds, cancellationToken);
        if (invitees.IsFailure)
        {
            return invitees.Error;
        }

        var plan = EventInvitationFeatures.BuildPlan(
            command.EventDate, command.StartsAt, command.EndsAt, command.IsAllDay, command.Mode, command.Location,
            command.OnlineUrl, command.NotifyInvitees, command.CategoryId);
        var updated = companyEvent.Update(command.Title, command.Description, command.Type, command.VisibilityOrgUnitId, plan);
        if (updated.IsFailure)
        {
            return updated;
        }

        // A null invitee list means "leave invitees untouched" (legacy callers); an explicit list replaces them.
        if (command.InviteeEmployeeIds is not null)
        {
            EventInvitationFeatures.NotifyIfRequested(outbox, companyEvent, companyEvent.SetInvitees(invitees.Value));
        }

        return updated;
    }
}

/// <summary>Manuel şirket etkinliğini sil (senseik-027).</summary>
[RequiresPermission("announcements.events.manage")]
public sealed record DeleteCompanyEventCommand(Guid EventId) : ICommand;

public sealed class DeleteCompanyEventHandler(ITenantContext tenant, ICompanyEventRepository events)
    : ICommandHandler<DeleteCompanyEventCommand>
{
    public async Task<Result> Handle(DeleteCompanyEventCommand command, CancellationToken cancellationToken)
    {
        var companyEvent = await events.GetByIdAsync(tenant.TenantId, command.EventId, cancellationToken);
        if (companyEvent is null)
        {
            return Error.NotFound(CompanyEventDomainErrors.NotFound);
        }

        events.Remove(companyEvent);
        return Result.Success();
    }
}
