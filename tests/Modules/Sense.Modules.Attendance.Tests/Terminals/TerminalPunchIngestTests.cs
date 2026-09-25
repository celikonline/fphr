using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Okutma ucu (Z5.1): kalem bazlı sonuç, kiracı yalnız terminalden, idempotans, yön modu, µs normalleştirme, son görülme.</summary>
public sealed class TerminalPunchIngestTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Occurred = new(2026, 9, 25, 8, 1, 2, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly FakePunchRecorder _recorder = new();
    private readonly ITerminalCredentialHasher _hasher = TerminalTestData.Hasher();
    private readonly List<EmployeeTerminalCredential> _credentials = [];
    private readonly TerminalIdentity _terminal;

    public TerminalPunchIngestTests()
    {
        _terminal = new TerminalIdentity(Guid.NewGuid(), _tenantId, TerminalDirectionMode.Both, Guid.NewGuid());
        AddCredential(_tenantId, _employeeA, TerminalCredentialKind.Card, "04A1B2C3");
        AddCredential(_tenantId, _employeeB, TerminalCredentialKind.Pin, "4321");
        _terminals.ListActiveCredentialsByValuesAsync(default, default!, default).ReturnsForAnyArgs(call =>
        {
            var tenant = call.ArgAt<Guid>(0);
            var values = call.ArgAt<IReadOnlyCollection<string>>(1);
            return _credentials.Where(c => c.TenantId == tenant && c.IsActive && values.Contains(c.Value)).ToList();
        });
    }

    private TerminalPunchIngest Ingest() => new(_terminals, _hasher, _recorder, new FakeTimeProvider(Now));

    private void AddCredential(Guid tenantId, Guid employeeId, TerminalCredentialKind kind, string raw) =>
        _credentials.Add(EmployeeTerminalCredential.Create(tenantId, employeeId, kind, _hasher.Hash(tenantId, kind, raw)).Value);

    private static TerminalPunchItem Item(TerminalCredentialKind? kind, string? value, DateTime? occurred = null, PunchDirectionDto? direction = null) =>
        new(new TerminalCredentialInput(kind, value), occurred ?? Occurred, direction);

    [Fact]
    public async Task Unknown_credential_reported_per_item_others_processed()
    {
        var result = await Ingest().IngestAsync(_terminal,
            [
                Item(TerminalCredentialKind.Card, "04A1B2C3", direction: PunchDirectionDto.In),
                Item(TerminalCredentialKind.Card, "DEADBEEF", direction: PunchDirectionDto.In),
                Item(TerminalCredentialKind.Pin, "4321", direction: PunchDirectionDto.Out),
            ], TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Accepted.ShouldBe(2);
        result.Value.Rejected.ShouldBe(1);
        result.Value.Items.ShouldBe(
            [
                new TerminalPunchItemResultDto(0, TerminalPunchItemStatus.Accepted),
                new TerminalPunchItemResultDto(1, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.UnknownCredential),
                new TerminalPunchItemResultDto(2, TerminalPunchItemStatus.Accepted),
            ]);
        _recorder.Received.Select(p => p.EmployeeId).ShouldBe([_employeeA, _employeeB]);
        _recorder.Received.ShouldAllBe(p => p.TenantId == _tenantId && p.Source == PunchSourceDto.Terminal && p.TerminalId == _terminal.TerminalId
            && p.LocationId == _terminal.LocationId && !p.IsManual && p.CreatedByUserId == null);
    }

    [Fact]
    public async Task Terminal_key_scopes_to_its_tenant_only()
    {
        var otherTenant = Guid.NewGuid();
        var foreignEmployee = Guid.NewGuid();
        AddCredential(otherTenant, foreignEmployee, TerminalCredentialKind.Card, "CAFE0001");

        var result = await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "CAFE0001")], TestContext.Current.CancellationToken);

        result.Value.Items.Single().ShouldBe(new TerminalPunchItemResultDto(0, TerminalPunchItemStatus.Rejected, TerminalPunchRejectReasons.UnknownCredential));
        _recorder.Received.ShouldBeEmpty();
        await _terminals.Received().ListActiveCredentialsByValuesAsync(_tenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await _terminals.DidNotReceive().ListActiveCredentialsByValuesAsync(otherTenant, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_tenant_credential_returned_by_a_faulty_lookup_is_still_not_used()
    {
        var otherTenant = Guid.NewGuid();
        var foreign = EmployeeTerminalCredential.Create(otherTenant, Guid.NewGuid(), TerminalCredentialKind.Card, _hasher.Hash(_tenantId, TerminalCredentialKind.Card, "CAFE0002")).Value;
        _terminals.ListActiveCredentialsByValuesAsync(default, default!, default).ReturnsForAnyArgs([foreign]);

        var result = await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "CAFE0002")], TestContext.Current.CancellationToken);

        result.Value.Items.Single().Reason.ShouldBe(TerminalPunchRejectReasons.UnknownCredential);
        _recorder.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Batch_idempotent_retry_returns_ignored_count()
    {
        TerminalPunchItem[] batch =
        [
            Item(TerminalCredentialKind.Card, "04A1B2C3", direction: PunchDirectionDto.In),
            Item(TerminalCredentialKind.Pin, "4321", direction: PunchDirectionDto.In),
        ];

        var first = await Ingest().IngestAsync(_terminal, batch, TestContext.Current.CancellationToken);
        var retry = await Ingest().IngestAsync(_terminal, batch, TestContext.Current.CancellationToken);

        (first.Value.Accepted, first.Value.Ignored, first.Value.Rejected).ShouldBe((2, 0, 0));
        (retry.Value.Accepted, retry.Value.Ignored, retry.Value.Rejected).ShouldBe((0, 2, 0));
        retry.Value.Items.ShouldAllBe(i => i.Status == TerminalPunchItemStatus.Ignored && i.Reason == null);
    }

    [Fact]
    public async Task OccurredAt_precision_normalized()
    {
        var withTicks = new DateTime(Occurred.Ticks + 1234, DateTimeKind.Utc); // 123.4 µs
        var sameMicrosecond = new DateTime(Occurred.Ticks + 1239, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(Occurred.AddMinutes(5), DateTimeKind.Unspecified);

        var first = await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "04A1B2C3", withTicks, PunchDirectionDto.In)], TestContext.Current.CancellationToken);
        var retry = await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "04A1B2C3", sameMicrosecond, PunchDirectionDto.In)], TestContext.Current.CancellationToken);
        await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "04A1B2C3", unspecified, PunchDirectionDto.In)], TestContext.Current.CancellationToken);

        _recorder.Received[0].OccurredAtUtc.ShouldBe(new DateTime(Occurred.Ticks + 1230, DateTimeKind.Utc));
        _recorder.Received[0].OccurredAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        first.Value.Accepted.ShouldBe(1);
        retry.Value.Ignored.ShouldBe(1);
        _recorder.Received[2].OccurredAtUtc.ShouldBe(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc));
        TerminalPunchIngest.NormalizeOccurredAt(new DateTimeOffset(Occurred).ToLocalTime().DateTime).ShouldBe(Occurred);
    }

    [Theory]
    [InlineData(TerminalDirectionMode.InOnly, null, PunchDirectionDto.In)]
    [InlineData(TerminalDirectionMode.OutOnly, null, PunchDirectionDto.Out)]
    [InlineData(TerminalDirectionMode.Both, null, PunchDirectionDto.Unknown)]
    [InlineData(TerminalDirectionMode.InOnly, PunchDirectionDto.Unknown, PunchDirectionDto.In)]
    [InlineData(TerminalDirectionMode.Both, PunchDirectionDto.Out, PunchDirectionDto.Out)]
    [InlineData(TerminalDirectionMode.InOnly, PunchDirectionDto.Out, PunchDirectionDto.Out)]
    public async Task Direction_mode_resolves_unknown_direction(TerminalDirectionMode mode, PunchDirectionDto? given, PunchDirectionDto expected)
    {
        var terminal = _terminal with { DirectionMode = mode };

        await Ingest().IngestAsync(terminal, [Item(TerminalCredentialKind.Card, "04A1B2C3", direction: given)], TestContext.Current.CancellationToken);

        _recorder.Received.Single().Direction.ShouldBe(expected);
    }

    [Fact]
    public async Task Heartbeat_updates_LastSeen()
    {
        var heartbeat = await Ingest().HeartbeatAsync(_terminal, TestContext.Current.CancellationToken);
        await Ingest().IngestAsync(_terminal, [Item(TerminalCredentialKind.Card, "04A1B2C3")], TestContext.Current.CancellationToken);

        heartbeat.Value.ServerTimeUtc.ShouldBe(Now);
        await _terminals.Received(2).MarkTerminalSeenAsync(_tenantId, _terminal.TerminalId, Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TerminalPunchIngest.MaxItemsPerRequest + 1)]
    public async Task Batch_size_outside_limits_is_a_validation_error(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => (TerminalPunchItem?)Item(TerminalCredentialKind.Card, "04A1B2C3", Occurred.AddSeconds(i))).ToList();

        var result = await Ingest().IngestAsync(_terminal, items, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(TerminalErrors.BatchSizeInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _recorder.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Malformed_items_rejected_per_item_and_recorder_errors_do_not_stop_the_batch()
    {
        var failing = Guid.NewGuid();
        AddCredential(_tenantId, failing, TerminalCredentialKind.ExternalId, "ext-9");
        _recorder.Failures[failing] = Error.Validation("attendance.punch.in_future");
        _recorder.LockedEmployees.Add(_employeeB);

        var result = await Ingest().IngestAsync(_terminal,
            [
                null,
                new TerminalPunchItem(null, Occurred, null),
                Item(null, "04A1B2C3"),
                Item(TerminalCredentialKind.Card, "  "),
                Item(TerminalCredentialKind.Card, "04A1B2C3", default(DateTime)),
                Item(TerminalCredentialKind.ExternalId, "ext-9"),
                Item(TerminalCredentialKind.Pin, "4321"),
                Item(TerminalCredentialKind.Card, " 04a1b2c3 "),
            ], TestContext.Current.CancellationToken);

        result.Value.Items.Select(i => i.Reason).ShouldBe(
            [
                TerminalPunchRejectReasons.InvalidItem, TerminalPunchRejectReasons.InvalidItem, TerminalPunchRejectReasons.InvalidItem,
                TerminalPunchRejectReasons.InvalidItem, TerminalPunchRejectReasons.InvalidItem, "attendance.punch.in_future",
                TerminalPunchRejectReasons.PeriodLocked, null,
            ]);
        (result.Value.Accepted, result.Value.Ignored, result.Value.Rejected).ShouldBe((1, 0, 7));
        _recorder.Received.Last().EmployeeId.ShouldBe(_employeeA);
    }
}
