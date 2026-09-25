using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>Invitee validation, plan building and invitation notification shared by create/update (E4).</summary>
public static class EventInvitationFeatures
{
    /// <summary>Builds the schedule plan; without <paramref name="startsAt"/> the legacy <paramref name="eventDate"/> becomes an all-day event.</summary>
    public static CompanyEventPlan BuildPlan(
        DateOnly eventDate, DateTime? startsAt, DateTime? endsAt, bool? isAllDay, CompanyEventMode? mode,
        string? location, string? onlineUrl, bool notifyInvitees, Guid? categoryId) =>
        new(
            startsAt ?? eventDate.ToDateTime(TimeOnly.MinValue),
            endsAt,
            isAllDay ?? startsAt is null,
            mode, location, onlineUrl, notifyInvitees, categoryId);

    /// <summary>Every invitee must be a known employee of the tenant; returns the de-duplicated ids.</summary>
    public static async Task<Result<IReadOnlyList<Guid>>> ValidateInviteesAsync(
        IEmployeeContactLookup contacts, Guid tenantId, IReadOnlyList<Guid>? inviteeIds, CancellationToken cancellationToken)
    {
        var distinct = (inviteeIds ?? []).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return Result.Success<IReadOnlyList<Guid>>(distinct);
        }

        var found = await contacts.GetContactsAsync(tenantId, distinct, cancellationToken);
        return found.Select(c => c.EmployeeId).Distinct().Count() == distinct.Count
            ? Result.Success<IReadOnlyList<Guid>>(distinct)
            : Error.Validation(CompanyEventDomainErrors.InviteeNotFound);
    }

    public static async Task<Result> ValidateCategoryAsync(
        ICompanyEventCategoryRepository categories, Guid tenantId, Guid? categoryId, CancellationToken cancellationToken) =>
        categoryId is { } id && await categories.GetByIdAsync(tenantId, id, cancellationToken) is null
            ? Error.Validation(CompanyEventDomainErrors.CategoryNotFound)
            : Result.Success();

    /// <summary>Enqueues the invitation event when the event asks to notify and someone new was invited.</summary>
    public static void NotifyIfRequested(IIntegrationEventOutbox outbox, CompanyEvent companyEvent, IReadOnlyList<Guid> newlyInvited)
    {
        if (!companyEvent.NotifyInvitees || newlyInvited.Count == 0 || companyEvent.StartsAt is not { } startsAt)
        {
            return;
        }

        outbox.Enqueue(new CompanyEventInvitationRequested(
            companyEvent.TenantId, companyEvent.Id, companyEvent.Title, startsAt, companyEvent.EndsAt,
            companyEvent.IsAllDay, companyEvent.Location, companyEvent.OnlineUrl, newlyInvited));
    }
}
