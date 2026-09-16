using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Infrastructure.BackgroundJobs;
using Sense.Shared.Contracts.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// <see cref="CandidateRetentionSweepJobHandler"/> (ADR-0004 IScheduledJobHandler, JobType.CandidateRetentionSweep,
/// Faz 9 TALIMAT tur açılışı madde 1): mocks <see cref="ICandidateRepository"/>/<see cref="IRecruitmentUnitOfWork"/>
/// with NSubstitute, same pattern as Documents' DocumentExpiryReminderJobHandlerTests.
/// </summary>
public sealed class CandidateRetentionSweepJobHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CandidateRetentionSweepJobHandler CreateHandler(
        ICandidateRepository candidates, IRecruitmentUnitOfWork unitOfWork) =>
        new(candidates, unitOfWork, TimeProvider.System, NullLogger<CandidateRetentionSweepJobHandler>.Instance);

    [Fact]
    public void Handles_IsCandidateRetentionSweep()
    {
        var handler = CreateHandler(Substitute.For<ICandidateRepository>(), Substitute.For<IRecruitmentUnitOfWork>());

        handler.Handles.ShouldBe(JobType.CandidateRetentionSweep);
    }

    [Fact]
    public async Task ExecuteAsync_NoTenantInContext_DoesNothing()
    {
        var candidates = Substitute.For<ICandidateRepository>();
        var unitOfWork = Substitute.For<IRecruitmentUnitOfWork>();
        var handler = CreateHandler(candidates, unitOfWork);

        await handler.ExecuteAsync(new ScheduledJobContext(null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        await candidates.DidNotReceive().ListRetentionExpiredAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NothingExpired_DoesNotSave()
    {
        var candidates = Substitute.For<ICandidateRepository>();
        candidates.ListRetentionExpiredAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Candidate>>([]));
        var unitOfWork = Substitute.For<IRecruitmentUnitOfWork>();
        var handler = CreateHandler(candidates, unitOfWork);

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HasExpiredCandidates_AnonymizesAndSaves()
    {
        var candidate = Candidate.Create(TenantId, "Ada", "Yılmaz", "ada.yilmaz@example.com", null).Value;
        var candidates = Substitute.For<ICandidateRepository>();
        candidates.ListRetentionExpiredAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Candidate>>([candidate]));
        var unitOfWork = Substitute.For<IRecruitmentUnitOfWork>();
        var handler = CreateHandler(candidates, unitOfWork);

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);

        candidate.IsAnonymized.ShouldBeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
