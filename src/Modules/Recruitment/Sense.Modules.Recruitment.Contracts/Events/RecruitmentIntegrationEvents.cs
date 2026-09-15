using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Recruitment.Contracts.Events;

/// <summary>Published (via Recruitment's outbox) the first time a posting is published. No subscriber yet in R0 - Notifications will consume this in a later package.</summary>
public sealed record JobPostingPublished(Guid TenantId, Guid JobPostingId, string Title) : IntegrationEvent(TenantId);

/// <summary>Published (via Recruitment's outbox) whenever an application moves to a new pipeline stage.</summary>
public sealed record JobApplicationStageChanged(Guid TenantId, Guid JobApplicationId, Guid JobPostingId, Guid CandidateId, string Stage)
    : IntegrationEvent(TenantId);
