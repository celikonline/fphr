using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Recruitment.Contracts.Events;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;

namespace Sense.Modules.Recruitment.Application.Applications;

/// <summary>
/// Consumes <see cref="JobApplicationStageChanged"/> integration event raised by
/// <see cref="MoveJobApplicationStageHandler"/> when an application transitions to a new pipeline stage.
/// On Hired transition, initiates employee provisioning via S-23 <see cref="IEmployeeProvisioning.CreateFromCandidateAsync"/>.
/// Note: DI registration deferred (implementation will be registered in RecruitmentModule.AddModule once
/// Faz 2 BE provides the <see cref="IEmployeeProvisioning"/> implementation).
/// </summary>
public sealed class JobApplicationStageChangedHandler(
    IJobApplicationRepository applications,
    ICandidateRepository candidates,
    IEmployeeProvisioning provisioning,
    IRecruitmentUnitOfWork unitOfWork,
    ILogger<JobApplicationStageChangedHandler> logger)
    : IIntegrationEventHandler<JobApplicationStageChanged>
{
    public async Task Handle(JobApplicationStageChanged @event, CancellationToken cancellationToken)
    {
        // Only process Hired transitions; other stage changes are not relevant to provisioning.
        if (@event.Stage != nameof(ApplicationStage.Hired))
        {
            return;
        }

        var application = await applications.GetByIdAsync(@event.JobApplicationId, cancellationToken);
        if (application.IsFailure)
        {
            // No-op if application not found; may have been cleaned up or is cross-module orphan.
            logger.LogWarning(
                "JobApplicationStageChangedHandler: application {JobApplicationId} not found for tenant {TenantId}",
                @event.JobApplicationId, @event.TenantId);
            return;
        }

        var candidate = await candidates.GetByIdAsync(@event.CandidateId, cancellationToken);
        if (candidate.IsFailure)
        {
            // No-op if candidate not found; same as above.
            logger.LogWarning(
                "JobApplicationStageChangedHandler: candidate {CandidateId} not found for tenant {TenantId}",
                @event.CandidateId, @event.TenantId);
            return;
        }

        // Build provisioning request from candidate and application context.
        var hireRequest = new CandidateHireRequest(
            FirstName: candidate.Value.FirstName,
            LastName: candidate.Value.LastName,
            Email: candidate.Value.Email,
            Phone: candidate.Value.Phone,
            NationalId: null, // Candidate record does not capture TCKN; employee created in Draft if missing.
            OrgUnitId: null, // Org unit determined during onboarding flow, not from ATS.
            PositionTitle: null, // Job posting title not carried here; title assigned during onboarding.
            HireDate: DateOnly.FromDateTime(DateTime.UtcNow), // Hire effective date: today (can be overridden in onboarding).
            SourceApplicationId: @event.JobApplicationId); // For idempotency and audit trail.

        var employeeId = await provisioning.CreateFromCandidateAsync(
            @event.TenantId,
            hireRequest,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "JobApplicationStageChangedHandler: candidate {CandidateId} hired; employee {EmployeeId} provisioned for tenant {TenantId}",
            @event.CandidateId, employeeId, @event.TenantId);
    }
}
