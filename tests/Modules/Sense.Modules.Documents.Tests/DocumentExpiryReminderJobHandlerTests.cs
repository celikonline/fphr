using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.BackgroundJobs;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Events;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// <see cref="DocumentExpiryReminderJobHandler"/> (ADR-0004 IScheduledJobHandler, JobType.DocumentExpiryReminder):
/// mocks the repository/outbox ports with NSubstitute, same pattern as the module's other handler tests.
/// </summary>
public sealed class DocumentExpiryReminderJobHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Handles_IsDocumentExpiryReminder()
    {
        var handler = new DocumentExpiryReminderJobHandler(
            Substitute.For<IEmployeeDocumentRequirementRepository>(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IDocumentsUnitOfWork>(),
            TimeProvider.System);

        handler.Handles.ShouldBe(JobType.DocumentExpiryReminder);
    }

    [Fact]
    public async Task ExecuteAsync_NoTenantInContext_DoesNothing()
    {
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new DocumentExpiryReminderJobHandler(requirements, outbox, unitOfWork, TimeProvider.System);

        await handler.ExecuteAsync(new ScheduledJobContext(null, null, DateTimeOffset.UtcNow), CancellationToken.None);

        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_OnlyDocumentsWithoutExpiryDate_DoesNotEnqueue()
    {
        // "Süresiz" belgeler (ExpiryDate == null) repository sorgusunun WHERE koşulunda zaten elenir (bkz.
        // ListExpiringAsync'in EF implementasyonu) - handler seviyesinde bunun karşılığı Total=0 dönmesidir, yani
        // fake repo burada "tenant'ın tüm belgeleri süresiz" senaryosunu da kapsar.
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), DocumentsLimits.DefaultExpiryReportWindowDays, Arg.Any<DocumentExpiryState?>(), 0, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 0)));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new DocumentExpiryReminderJobHandler(requirements, outbox, unitOfWork, TimeProvider.System);

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);

        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HasExpiringAndExpired_EnqueuesCountsAndSaves()
    {
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expiring, 0, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 3)));
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, 0, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 2)));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new DocumentExpiryReminderJobHandler(requirements, outbox, unitOfWork, TimeProvider.System);

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);

        outbox.Received(1).Enqueue(Arg.Is<DocumentExpiryReminderDue>(e => e.TenantId == TenantId && e.ExpiringCount == 3 && e.ExpiredCount == 2));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Fake zaman: <see cref="DocumentExpiryReminderJobHandler"/> "bugün"ü <c>clock.GetUtcNow()</c>'dan türetir
    /// (<c>TimeProvider.System</c> değil) - "30 gün içinde dolacak" penceresinin doğru ankraja (gerçek saat değil,
    /// enjekte edilen saat) oturduğunu kanıtlamak için ikisi de aynı sabit tarihi görmeli.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UsesInjectedClockDate_ForBothExpiringAndExpiredQueries()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var expectedToday = DateOnly.FromDateTime(fixedNow.UtcDateTime);
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(expectedToday, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expiring, 0, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 1)));
        requirements
            .ListExpiringAsync(expectedToday, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, 0, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 0)));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new DocumentExpiryReminderJobHandler(requirements, outbox, unitOfWork, new FixedTimeProvider(fixedNow));

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);

        await requirements.Received(1).ListExpiringAsync(expectedToday, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expiring, 0, 1, Arg.Any<CancellationToken>());
        await requirements.Received(1).ListExpiringAsync(expectedToday, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, 0, 1, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(Arg.Is<DocumentExpiryReminderDue>(e => e.TenantId == TenantId && e.ExpiringCount == 1 && e.ExpiredCount == 0));
    }

    /// <summary>Test-only fake clock (mirrors <c>Sense.Modules.Identity.Tests.Security.TotpServiceTests.FixedTimeProvider</c> - no FakeTimeProvider package reference in this test project).</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
