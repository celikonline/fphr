using NSubstitute;
using Sense.Modules.Identity.Application.Privacy;
using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Contracts.Privacy;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Privacy;

/// <summary>TB-3 tur 1 (DB'siz dilim): ConsentRecorder'ın "aynı özne+tür için yeni kayıt eskisini kapatır" iş kuralı.</summary>
public sealed class ConsentRecorderTests
{
    private readonly IConsentRecordRepository _repository = Substitute.For<IConsentRecordRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task RecordAsync_NoActiveRecord_AddsNewOneAndDoesNotSupersedeAnything()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.KvkkNotice, Arg.Any<CancellationToken>())
            .Returns((ConsentRecord?)null);

        var request = new RecordConsent(tenantId, ConsentSubjectType.User, subjectId, ConsentType.KvkkNotice, "2026-09", now, "10.0.0.1");
        var id = await recorder.RecordAsync(request, CancellationToken.None);

        id.ShouldNotBe(Guid.Empty);
        ConsentRecord? added = null;
        _repository.Received(1).Add(Arg.Do<ConsentRecord>(r => added = r));
        added.ShouldNotBeNull();
        added!.Id.ShouldBe(id);
        added.IsActive.ShouldBeTrue();
        added.NoticeVersion.ShouldBe("2026-09");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_ActiveRecordExists_SupersedesTheOldOne_BeforeAddingTheNewOne()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var acceptedAt = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var previous = ConsentRecord.Create(new RecordConsent(
            tenantId, ConsentSubjectType.User, subjectId, ConsentType.KvkkNotice, "2026-01", acceptedAt.AddMonths(-8), null));
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.KvkkNotice, Arg.Any<CancellationToken>())
            .Returns(previous);

        var request = new RecordConsent(tenantId, ConsentSubjectType.User, subjectId, ConsentType.KvkkNotice, "2026-09", acceptedAt, null);
        await recorder.RecordAsync(request, CancellationToken.None);

        previous.IsActive.ShouldBeFalse();
        previous.SupersededAt.ShouldBe(acceptedAt);
        _repository.Received(1).Add(Arg.Is<ConsentRecord>(r => r.NoticeVersion == "2026-09" && r.IsActive));
    }

    [Fact]
    public async Task HasActiveConsentAsync_ReflectsRepositoryLookup()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.Candidate, subjectId, ConsentType.CandidatePool, Arg.Any<CancellationToken>())
            .Returns((ConsentRecord?)null, ConsentRecord.Create(new RecordConsent(
                tenantId, ConsentSubjectType.Candidate, subjectId, ConsentType.CandidatePool, "2026-09", DateTimeOffset.UtcNow, null)));

        (await recorder.HasActiveConsentAsync(tenantId, ConsentSubjectType.Candidate, subjectId, ConsentType.CandidatePool, CancellationToken.None))
            .ShouldBeFalse();
        (await recorder.HasActiveConsentAsync(tenantId, ConsentSubjectType.Candidate, subjectId, ConsentType.CandidatePool, CancellationToken.None))
            .ShouldBeTrue();
    }
}
