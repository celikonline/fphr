using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.AuditLog;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.AuditLog;

/// <summary>
/// S-1 sözleşmesi: <c>identity.audit.sensitive_view</c> yoksa <c>Details</c> maskelenir, diğer alanlar
/// (Actor/Action/Resource/…) değişmeden kalır. DB'ye dokunmaz - gerçek Postgres'e karşı round-trip testi
/// IdentityAuditLogInterceptorIntegrationTests'te (bkz. o dosya).
/// senseik-018 (E-01): Source aynı kuralı izler - "kim yaptı" gibi "nereden geldi" de maskeleme dışıdır.
/// senseik-049: handler artık tek bir IAuditLogReadStore değil, birden çok IAuditLogSource'u birleştirir (Identity +
/// Employee/Leave/Workflow) - bu testler o birleştirme/sıralama/sayfalama davranışını da kapsar.
/// </summary>
public sealed class GetAuditLogHandlerTests
{
    private static readonly AuditLogEntrySnapshot SampleEntry = new(
        Guid.NewGuid(), DateTimeOffset.UtcNow, "ayse@test.local", "update", "User", "u-1", "IBAN TR00... -> TR11...", AuditSources.Web);

    private static IAuditLogSource SourceReturning(params AuditLogEntrySnapshot[] entries)
    {
        var source = Substitute.For<IAuditLogSource>();
        source.QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AuditLogEntrySnapshot>)entries);
        return source;
    }

    [Fact]
    public async Task Handle_WithoutSensitiveViewPermission_MasksDetailsOnly()
    {
        var source = SourceReturning(SampleEntry);

        var userId = Guid.NewGuid();
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, Arg.Any<CancellationToken>()).Returns(false);
        var currentUser = new AuthenticatedUser(userId, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler([source], currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value.Items.Single();
        entry.Details.ShouldBe(AuditLogMasking.MaskedDetails);
        entry.Actor.ShouldBe(SampleEntry.Actor);
        entry.Action.ShouldBe(SampleEntry.Action);
        entry.Resource.ShouldBe(SampleEntry.Resource);
        entry.ResourceId.ShouldBe(SampleEntry.ResourceId);
        // Regression (senseik-018 §4): masking must stay scoped to Details - Actor/Source are the "who/how" KVKK
        // m.12 requires to stay visible even without identity.audit.sensitive_view.
        entry.Source.ShouldBe(SampleEntry.Source);
    }

    [Fact]
    public async Task Handle_WithSensitiveViewPermission_ReturnsDetailsUnmasked()
    {
        var source = SourceReturning(SampleEntry);

        var userId = Guid.NewGuid();
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, Arg.Any<CancellationToken>()).Returns(true);
        var currentUser = new AuthenticatedUser(userId, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler([source], currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize), CancellationToken.None);

        result.Value.Items.Single().Details.ShouldBe(SampleEntry.Details);
    }

    [Fact]
    public async Task Handle_InvalidPageOrPageSize_FallsBackToDefaults()
    {
        var source = SourceReturning();

        var permissions = Substitute.For<IPermissionService>();
        var currentUser = new AuthenticatedUser(Guid.NewGuid(), null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler([source], currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 0, -5), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(1);
        result.Value.PageSize.ShouldBe(GetAuditLogHandler.DefaultPageSize);
    }

    /// <summary>senseik-049: iki farklı modülden (Identity + Employee) gelen kayıtlar tek bir zaman sıralı sayfada birleşir.</summary>
    [Fact]
    public async Task Handle_MergesMultipleSources_OrderedByOccurredAtDescending()
    {
        var older = SampleEntry with { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), Resource = "User" };
        var newer = SampleEntry with { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, Resource = "Employee" };

        var identitySource = SourceReturning(older);
        var employeeSource = SourceReturning(newer);

        var permissions = Substitute.For<IPermissionService>();
        var userId = Guid.NewGuid();
        permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, Arg.Any<CancellationToken>()).Returns(true);
        var currentUser = new AuthenticatedUser(userId, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler([identitySource, employeeSource], currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize), CancellationToken.None);

        result.Value.TotalCount.ShouldBe(2);
        result.Value.Items.Select(e => e.Resource).ShouldBe(["Employee", "User"]);
    }
}
