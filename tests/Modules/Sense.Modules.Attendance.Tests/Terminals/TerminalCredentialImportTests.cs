using System.Text.Json;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// "terminal-credentials" içe aktarma (Z5.4): değer yalnız HMAC özetiyle saklanır; aynı çalışana aynı kimlik yoksayılır; başka çalışanda aktif
/// kimlik sahip bilgisi sızdırmayan satır hatasıdır; pasif kimlik devredilir; biçim/sicil/işten ayrılma satır hatalarıdır; pepper yoksa şablon 503.
/// </summary>
public sealed class TerminalCredentialImportTests
{
    private const string Sicil = "S-1001";
    private const string OtherSicil = "S-2002";
    private const string Pin = "471193";
    private const string Card = "04A1B2C3D4";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employees = Substitute.For<IEmployeeLookup>();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly List<EmployeeTerminalCredential> _added = [];
    private readonly List<EmployeeTerminalCredential> _stored = [];

    public TerminalCredentialImportTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _employees.FindBySicilNumarasiAsync(_tenantId, Sicil, Arg.Any<CancellationToken>()).Returns(new EmployeeSicilMatch(_employeeId, false));
        _employees.FindBySicilNumarasiAsync(_tenantId, OtherSicil, Arg.Any<CancellationToken>()).Returns(new EmployeeSicilMatch(_otherEmployeeId, false));
        _terminals.When(t => t.AddCredential(Arg.Any<EmployeeTerminalCredential>())).Do(call => _added.Add(call.Arg<EmployeeTerminalCredential>()));
        _terminals.ListCredentialsByValuesAsync(default!, default).ReturnsForAnyArgs(call =>
            _stored.Where(c => call.Arg<IReadOnlyCollection<string>>().Contains(c.Value)).ToList());
    }

    [Fact]
    public async Task Import_row_hashes_value_and_never_stores_plaintext()
    {
        var result = await Handler().ProcessAsync(Batch(Row(2, Sicil, "PIN", Pin)), TestContext.Current.CancellationToken);

        var row = result.Rows.ShouldHaveSingleItem();
        row.Outcome.ShouldBe(ImportRowOutcome.Inserted);
        row.MatchKey.ShouldBe(Sicil);
        var credential = _added.ShouldHaveSingleItem();
        credential.Value.ShouldBe(Digest(TerminalCredentialKind.Pin, Pin));
        credential.Value.ShouldNotContain(Pin);
        (credential.EmployeeId, credential.Kind, credential.IsActive, credential.TenantId).ShouldBe((_employeeId, TerminalCredentialKind.Pin, true, _tenantId));
        row.TargetEntityId.ShouldBe(credential.Id);
        result.Applied.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        var echoed = JsonSerializer.Serialize(result);
        echoed.ShouldNotContain(Pin);
        echoed.ShouldNotContain(credential.Value);
    }

    [Fact]
    public async Task Same_credential_same_employee_is_ignored()
    {
        _stored.Add(Stored(_employeeId, TerminalCredentialKind.Card, "04A1B2C3D4", active: true));

        var result = await Handler().ProcessAsync(
            Batch(Row(2, Sicil, "Kart", "04a1b2c3d4"), Row(3, Sicil, "PIN", Pin), Row(4, Sicil, "pin", " 471193 ")),
            TestContext.Current.CancellationToken);

        result.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Skipped, ImportRowOutcome.Inserted, ImportRowOutcome.Skipped]);
        _added.ShouldHaveSingleItem().Kind.ShouldBe(TerminalCredentialKind.Pin);
        _stored[0].EmployeeId.ShouldBe(_employeeId);
    }

    [Fact]
    public async Task Active_credential_of_other_employee_is_row_error_without_leaking_owner()
    {
        var owned = Stored(_otherEmployeeId, TerminalCredentialKind.Card, Card, active: true);
        _stored.Add(owned);

        var result = await Handler().ProcessAsync(
            Batch(Row(2, Sicil, "Kart", Card), Row(3, OtherSicil, "Kart", "C-9090"), Row(4, Sicil, "Kart", "c-9090")),
            TestContext.Current.CancellationToken);

        var conflict = result.Rows[0];
        conflict.Outcome.ShouldBe(ImportRowOutcome.Error);
        conflict.MatchKey.ShouldBe(Sicil);
        conflict.TargetEntityId.ShouldBeNull();
        conflict.Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, AttendanceDomainErrors.TerminalCredentialInUse)]);
        var echoed = JsonSerializer.Serialize(conflict);
        foreach (var leak in new[] { _otherEmployeeId.ToString(), owned.Id.ToString(), OtherSicil, owned.Value, Card })
        {
            echoed.ShouldNotContain(leak);
        }

        // Aynı dosyada önce başka çalışana atanan kimlik de artık aktif: ikinci atama aynı satır hatasıdır.
        result.Rows[1].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        result.Rows[2].Errors.Single().Code.ShouldBe(AttendanceDomainErrors.TerminalCredentialInUse);
        (owned.EmployeeId, owned.IsActive).ShouldBe((_otherEmployeeId, true));
        _added.ShouldHaveSingleItem().EmployeeId.ShouldBe(_otherEmployeeId);
    }

    [Fact]
    public async Task Pin_in_use_by_other_employee_is_a_generic_rejection()
    {
        var owned = Stored(_otherEmployeeId, TerminalCredentialKind.Pin, Pin, active: true);
        _stored.Add(owned);

        var result = await Handler().ProcessAsync(Batch(Row(2, Sicil, "PIN", Pin)), TestContext.Current.CancellationToken);

        var row = result.Rows.ShouldHaveSingleItem();
        (row.Outcome, row.MatchKey, row.TargetEntityId).ShouldBe((ImportRowOutcome.Error, Sicil, (Guid?)null));
        row.Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, TerminalCredentialErrors.Rejected)]);
        JsonSerializer.Serialize(row).ShouldNotContain(AttendanceDomainErrors.TerminalCredentialInUse);
        (owned.EmployeeId, owned.IsActive).ShouldBe((_otherEmployeeId, true));
        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inactive_credential_of_other_employee_is_reassigned()
    {
        var lost = Stored(_otherEmployeeId, TerminalCredentialKind.ExternalId, "EXT-77", active: false);
        _stored.Add(lost);

        var result = await Handler().ProcessAsync(Batch(Row(2, Sicil, "Harici", "EXT-77")), TestContext.Current.CancellationToken);

        var row = result.Rows.ShouldHaveSingleItem();
        (row.Outcome, row.TargetEntityId).ShouldBe((ImportRowOutcome.Updated, (Guid?)lost.Id));
        (lost.EmployeeId, lost.IsActive).ShouldBe((_employeeId, true));
        _added.ShouldBeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("PIN", "048215", null)]
    [InlineData("PIN", "12345678", null)]
    [InlineData("PIN", "12345", TerminalCredentialErrors.PinInvalid)]
    [InlineData("PIN", "123456789", TerminalCredentialErrors.PinInvalid)]
    [InlineData("PIN", "12a456", TerminalCredentialErrors.PinInvalid)]
    [InlineData("PIN", "12 3456", TerminalCredentialErrors.PinInvalid)]
    [InlineData("PIN", "١٢٣٤٥٦", TerminalCredentialErrors.PinInvalid)]
    [InlineData("Kart", "A", null)]
    [InlineData("Harici", "x", null)]
    public async Task Pin_format_validation(string kind, string value, string? expectedError)
    {
        var result = await Handler().ProcessAsync(Batch(Row(2, Sicil, kind, value)), TestContext.Current.CancellationToken);

        var row = result.Rows.ShouldHaveSingleItem();
        if (expectedError is null)
        {
            row.Outcome.ShouldBe(ImportRowOutcome.Inserted);
            return;
        }

        row.Outcome.ShouldBe(ImportRowOutcome.Error);
        row.Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, expectedError)]);
        JsonSerializer.Serialize(row).ShouldNotContain(value);
        _added.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("4711")]
    [InlineData("12345")]
    public async Task Pin_shorter_than_6_rejected(string pin)
    {
        var imported = await Handler().ProcessAsync(Batch(Row(2, Sicil, "PIN", pin)), TestContext.Current.CancellationToken);
        var added = await AddHandler().Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, pin), TestContext.Current.CancellationToken);

        imported.Rows.ShouldHaveSingleItem().Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, TerminalCredentialErrors.PinInvalid)]);
        added.Error.ShouldBe(Error.Validation(TerminalCredentialErrors.PinInvalid));
        _added.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("12345a")]
    [InlineData("-123456")]
    [InlineData("1234.56")]
    [InlineData("１２３４５６")]
    public async Task Pin_non_digit_rejected(string pin)
    {
        var imported = await Handler().ProcessAsync(Batch(Row(2, Sicil, "PIN", pin)), TestContext.Current.CancellationToken);
        var added = await AddHandler().Handle(new AddEmployeeTerminalCredentialCommand(_employeeId, TerminalCredentialKind.Pin, pin), TestContext.Current.CancellationToken);

        imported.Rows.ShouldHaveSingleItem().Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, TerminalCredentialErrors.PinInvalid)]);
        added.Error.ShouldBe(Error.Validation(TerminalCredentialErrors.PinInvalid));
        _added.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Kart")]
    [InlineData("Harici")]
    public async Task Card_and_external_values_are_limited_to_64_characters(string kind)
    {
        var result = await Handler().ProcessAsync(
            Batch(Row(2, Sicil, kind, new string('7', TerminalCredentialRules.ValueMaxLength)), Row(3, Sicil, kind, new string('8', TerminalCredentialRules.ValueMaxLength + 1))),
            TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        result.Rows[1].Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, TerminalCredentialErrors.ValueTooLong)]);
    }

    [Fact]
    public async Task Unknown_registration_row_error()
    {
        var result = await Handler().ProcessAsync(Batch(Row(2, "S-YOK", "PIN", Pin), Row(3, Sicil, "PIN", "555512")), TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialErrors.EmployeeNotFound)]);
        result.Rows[1].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        result.Applied.ShouldBeTrue();
    }

    [Fact]
    public async Task Terminated_employee_row_error()
    {
        _employees.FindBySicilNumarasiAsync(_tenantId, "S-LEFT", Arg.Any<CancellationToken>()).Returns(new EmployeeSicilMatch(Guid.NewGuid(), IsTerminated: true));

        var result = await Handler().ProcessAsync(Batch(Row(2, "S-LEFT", "PIN", Pin)), TestContext.Current.CancellationToken);

        var row = result.Rows.ShouldHaveSingleItem();
        row.Outcome.ShouldBe(ImportRowOutcome.Error);
        row.Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialErrors.EmployeeNotActive)]);
        row.TargetEntityId.ShouldBeNull();
        _added.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_kind_and_missing_cells_are_row_errors_that_never_echo_the_cell()
    {
        var result = await Handler().ProcessAsync(
            Batch(Row(2, Sicil, Pin, "1234"), Row(3, Sicil, "PIN", null), Row(4, null, "PIN", Pin)), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Kind, TerminalCredentialErrors.KindInvalid)]);
        result.Rows[1].Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.Value, ImportErrorCodes.RequiredMissing)]);
        result.Rows[2].Errors.ShouldBe([new ImportRowError(TerminalCredentialImportColumns.SicilNumarasi, ImportErrorCodes.RequiredMissing)]);
        result.Rows.SelectMany(r => r.Errors).ShouldAllBe(e => e.Args == null);
        JsonSerializer.Serialize(result).ShouldNotContain(Pin);
    }

    [Fact]
    public async Task All_or_nothing_saves_nothing_when_any_row_fails()
    {
        _stored.Add(Stored(_otherEmployeeId, TerminalCredentialKind.Pin, Pin, active: true));

        var result = await Handler().ProcessAsync(
            Batch(ImportFailurePolicy.AllOrNothing, Row(2, Sicil, "PIN", "555512"), Row(3, Sicil, "PIN", Pin)), TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.Select(r => r.Outcome).ShouldBe([ImportRowOutcome.Skipped, ImportRowOutcome.Error]);
        result.Rows[0].TargetEntityId.ShouldBeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pepper_missing_returns_503_before_any_row()
    {
        var template = new TerminalCredentialImportTemplate(TerminalTestData.Hasher(pepper: null));

        var ready = template.CheckReady();

        ready.Error.Type.ShouldBe(ErrorType.ServiceUnavailable);
        ready.Error.Code.ShouldBe(TerminalErrors.CredentialPepperMissing);
        HttpStatusMap.For(ready.Error.Type).ShouldBe(503);
        new TerminalCredentialImportTemplate(TerminalTestData.Hasher()).CheckReady().IsSuccess.ShouldBeTrue();

        // Doğrudan çağrıda bile (DataTransfer önce CheckReady'ye bakar) hiçbir satır çözülmez/yazılmaz.
        var direct = await Handler(TerminalTestData.Hasher(pepper: null)).ProcessAsync(Batch(Row(2, Sicil, "PIN", Pin)), TestContext.Current.CancellationToken);
        direct.Applied.ShouldBeFalse();
        direct.Rows.ShouldHaveSingleItem().Errors.Single().Code.ShouldBe(TerminalErrors.CredentialPepperMissing);
        await _employees.DidNotReceiveWithAnyArgs().FindBySicilNumarasiAsync(default, default!, TestContext.Current.CancellationToken);
        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Template_marks_only_the_value_column_sensitive_and_requires_terminal_manage()
    {
        var template = new TerminalCredentialImportTemplate(TerminalTestData.Hasher());

        var columns = await template.GetColumnsAsync(TestContext.Current.CancellationToken);

        template.Code.ShouldBe("terminal-credentials");
        template.RequiredPermission.ShouldBe(AttendancePermissions.TerminalManage.Key);
        columns.Select(c => (c.Key, c.IsMatchKey, c.IsSensitive)).ShouldBe(
        [
            (TerminalCredentialImportColumns.SicilNumarasi, true, false),
            (TerminalCredentialImportColumns.Kind, false, false),
            (TerminalCredentialImportColumns.Value, false, true),
        ]);
        columns[1].Options.ShouldBe(["Kart", "PIN", "Harici"]);
        template.GetSampleRows(columns).Count.ShouldBe(3);
    }

    private TerminalCredentialImportRowHandler Handler(ITerminalCredentialHasher? hasher = null) =>
        new(_tenant, _employees, _terminals, hasher ?? TerminalTestData.Hasher(), _unitOfWork);

    private AddEmployeeTerminalCredentialHandler AddHandler() => new(_terminals, TerminalTestData.Hasher(), TerminalTestData.Contacts(), _tenant);

    private string Digest(TerminalCredentialKind kind, string raw) => TerminalTestData.Hasher().Digest(_tenantId, kind, raw);

    private EmployeeTerminalCredential Stored(Guid employeeId, TerminalCredentialKind kind, string raw, bool active)
    {
        var credential = EmployeeTerminalCredential.Create(_tenantId, employeeId, kind, Digest(kind, raw)).Value;
        if (!active)
        {
            credential.Deactivate();
        }

        return credential;
    }

    private static ImportBatch Batch(params ImportRow[] rows) => Batch(ImportFailurePolicy.SkipInvalidRows, rows);

    private static ImportBatch Batch(ImportFailurePolicy policy, params ImportRow[] rows) => new(
        ImportMode.Upsert,
        policy,
        [TerminalCredentialImportColumns.SicilNumarasi, TerminalCredentialImportColumns.Kind, TerminalCredentialImportColumns.Value],
        rows);

    private static ImportRow Row(int number, string? sicil, string? kind, string? value) => new(number, new Dictionary<string, ImportCell>
    {
        [TerminalCredentialImportColumns.SicilNumarasi] = new(sicil),
        [TerminalCredentialImportColumns.Kind] = new(kind),
        [TerminalCredentialImportColumns.Value] = new(value),
    });
}
