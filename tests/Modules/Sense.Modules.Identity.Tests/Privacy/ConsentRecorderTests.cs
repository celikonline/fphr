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
        _repository.Received(1).Add(Arg.Any<ConsentRecord>());
        var added = (ConsentRecord)_repository.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IConsentRecordRepository.Add))
            .GetArguments()[0]!;
        added.ShouldNotBeNull();
        added.Id.ShouldBe(id);
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

    [Fact]
    public async Task WithdrawAsync_ActiveRecordExists_WithdrawsItAndSaves()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var withdrawnAt = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var active = ConsentRecord.Create(new RecordConsent(
            tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, "2026-09", withdrawnAt.AddDays(-10), null));
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, Arg.Any<CancellationToken>())
            .Returns(active);

        var result = await recorder.WithdrawAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, withdrawnAt, CancellationToken.None);

        result.ShouldBeTrue();
        active.IsActive.ShouldBeFalse();
        active.WithdrawnAt.ShouldBe(withdrawnAt);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithdrawAsync_NoActiveRecord_ReturnsFalseAndDoesNotSave()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, Arg.Any<CancellationToken>())
            .Returns((ConsentRecord?)null);

        var result = await recorder.WithdrawAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, DateTimeOffset.UtcNow, CancellationToken.None);

        result.ShouldBeFalse();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_AfterWithdrawal_CreatesNewActiveRecord()
    {
        var recorder = new ConsentRecorder(_repository, _unitOfWork);
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var withdrawnAt = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var withdrawn = ConsentRecord.Create(new RecordConsent(
            tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, "2026-01", withdrawnAt.AddDays(-30), null));
        withdrawn.Withdraw(withdrawnAt);

        // Geri alınmış kayıt artık aktif olmadığı için repository "aktif kayıt yok" döner (SupersededAt olmasa da).
        _repository.FindActiveAsync(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, Arg.Any<CancellationToken>())
            .Returns((ConsentRecord?)null);

        var request = new RecordConsent(tenantId, ConsentSubjectType.User, subjectId, ConsentType.Marketing, "2026-09", withdrawnAt.AddDays(1), null);
        var id = await recorder.RecordAsync(request, CancellationToken.None);

        id.ShouldNotBe(Guid.Empty);
        _repository.Received(1).Add(Arg.Any<ConsentRecord>());
        var added = (ConsentRecord)_repository.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IConsentRecordRepository.Add))
            .GetArguments()[0]!;
        added.ShouldNotBeNull();
        added.IsActive.ShouldBeTrue();
        added.WithdrawnAt.ShouldBeNull();
    }
}

/// <summary>G-1-09: <see cref="ConsentRecord.Create"/> ispat kolonlarında (tür/sürüm/özne) boş veya varsayılan
/// değer kabul etmez - modülün mevcut domain hata kalıbı (<c>Sense.Shared.Kernel.Guard</c>).</summary>
public sealed class ConsentRecordCreateInvariantTests
{
    private static RecordConsent ValidRequest() => new(
        Guid.NewGuid(), ConsentSubjectType.User, Guid.NewGuid(), ConsentType.KvkkNotice, "2026-09", DateTimeOffset.UtcNow, null);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankConsentType_ThrowsArgumentException(string consentType)
    {
        var request = ValidRequest() with { ConsentType = consentType };

        Should.Throw<ArgumentException>(() => ConsentRecord.Create(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankNoticeVersion_ThrowsArgumentException(string noticeVersion)
    {
        var request = ValidRequest() with { NoticeVersion = noticeVersion };

        Should.Throw<ArgumentException>(() => ConsentRecord.Create(request));
    }

    [Fact]
    public void Create_EmptySubjectId_ThrowsArgumentException()
    {
        var request = ValidRequest() with { SubjectId = Guid.Empty };

        Should.Throw<ArgumentException>(() => ConsentRecord.Create(request));
    }
}
