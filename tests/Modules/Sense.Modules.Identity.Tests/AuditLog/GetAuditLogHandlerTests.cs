using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.AuditLog;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.AuditLog;

/// <summary>
/// S-1 sözleşmesi: <c>identity.audit.sensitive_view</c> yoksa <c>Details</c> maskelenir, diğer alanlar
/// (Actor/Action/Resource/…) değişmeden kalır. DB'ye dokunmaz (bkz. status/faz-01/MIGRATION-ISTEGI.md —
/// migration henüz yok, gerçek Postgres'e karşı round-trip testi migration uygulandıktan sonra eklenecek).
/// senseik-018 (E-01): Source aynı kuralı izler - "kim yaptı" gibi "nereden geldi" de maskeleme dışıdır.
/// </summary>
public sealed class GetAuditLogHandlerTests
{
    private static readonly AuditLogEntryDto SampleEntry = new(
        Guid.NewGuid(), DateTimeOffset.UtcNow, "ayse@test.local", "update", "User", "u-1", "IBAN TR00... -> TR11...", AuditSources.Web);

    [Fact]
    public async Task Handle_WithoutSensitiveViewPermission_MasksDetailsOnly()
    {
        var store = Substitute.For<IAuditLogReadStore>();
        store.ListAsync(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<AuditLogEntryDto>([SampleEntry], 1, GetAuditLogHandler.DefaultPageSize, 1));

        var userId = Guid.NewGuid();
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, Arg.Any<CancellationToken>()).Returns(false);
        var currentUser = new AuthenticatedUser(userId, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler(store, currentUser, permissions);
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
        var store = Substitute.For<IAuditLogReadStore>();
        store.ListAsync(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<AuditLogEntryDto>([SampleEntry], 1, GetAuditLogHandler.DefaultPageSize, 1));

        var userId = Guid.NewGuid();
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(userId, IdentityPermissions.AuditSensitiveView, Arg.Any<CancellationToken>()).Returns(true);
        var currentUser = new AuthenticatedUser(userId, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler(store, currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize), CancellationToken.None);

        result.Value.Items.Single().Details.ShouldBe(SampleEntry.Details);
    }

    [Fact]
    public async Task Handle_InvalidPageOrPageSize_FallsBackToDefaults()
    {
        var store = Substitute.For<IAuditLogReadStore>();
        store.ListAsync(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<AuditLogEntryDto>([], 1, GetAuditLogHandler.DefaultPageSize, 0));

        var permissions = Substitute.For<IPermissionService>();
        var currentUser = new AuthenticatedUser(Guid.NewGuid(), null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), false, null, null, null, null);

        var handler = new GetAuditLogHandler(store, currentUser, permissions);
        var result = await handler.Handle(new GetAuditLogQuery(null, null, null, null, 0, -5), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await store.Received(1).ListAsync(null, null, null, null, 1, GetAuditLogHandler.DefaultPageSize, Arg.Any<CancellationToken>());
    }
}
