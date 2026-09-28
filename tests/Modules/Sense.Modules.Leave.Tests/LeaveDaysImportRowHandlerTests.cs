using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Import;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveDaysImportRowHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IEmployeeLeaveProfileRepository _profiles = Substitute.For<IEmployeeLeaveProfileRepository>();
    private readonly ILeaveRuleSetRepository _ruleSets = Substitute.For<ILeaveRuleSetRepository>();
    private readonly ILeaveRequestRepository _requests = Substitute.For<ILeaveRequestRepository>();
    private readonly ILeaveLedgerRepository _ledger = Substitute.For<ILeaveLedgerRepository>();
    private readonly ILeaveUnitOfWork _unitOfWork = Substitute.For<ILeaveUnitOfWork>();
    private readonly LeaveRuleSet _ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(Guid.NewGuid(), StatutoryLeaveDurations.Turkey2026);
    private readonly EmployeeLeaveProfile _profile;

    private const string NationalId = "10000000146";
    private const string AnnualTypeLabel = "Yıllık izin";
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateOnly End = new(2026, 9, 5);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public LeaveDaysImportRowHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _profile = EmployeeLeaveProfile.Create(_tenantId, _employeeId, _ruleSet.Id, new DateOnly(2020, 1, 1), leaveAccrualStartDate: null);
        _profiles.FindByEmployeeIdAsync(_employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeLeaveProfile?>(_profile));
        _ruleSets.GetByIdAsync(_ruleSet.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sense.Shared.Kernel.Results.Result.Success(_ruleSet)));
        _ledger.ListAsync(_employeeId, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LeaveLedgerEntry>>([]));
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
    }

    private static ILegislationParameterReader EmptyLegislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));
        return reader;
    }

    private LeaveDaysImportRowHandler CreateHandler() =>
        new(_tenant, _employeeLookup, _profiles, _ruleSets, _requests, _ledger, new LeaveBalanceService(_ruleSets, _ledger, EmptyLegislation(), new FixedTimeProvider(DateTimeOffset.UtcNow)), _unitOfWork, new FixedTimeProvider(DateTimeOffset.UtcNow));

    private static ImportRow Row(int rowNumber, string? nationalId, string? typeName, string? start, string? end, string? days, string? description = null)
    {
        var values = new Dictionary<string, ImportCell>();
        void Set(string key, string? value)
        {
            if (value is not null)
            {
                values[key] = new ImportCell(value);
            }
        }

        Set(LeaveDaysImportColumns.NationalId, nationalId);
        Set(LeaveDaysImportColumns.LeaveTypeName, typeName);
        Set(LeaveDaysImportColumns.StartDate, start);
        Set(LeaveDaysImportColumns.EndDate, end);
        Set(LeaveDaysImportColumns.Days, days);
        Set(LeaveDaysImportColumns.Description, description);
        return new ImportRow(rowNumber, values);
    }

    [Fact]
    public async Task ProcessAsync_ValidRow_InsertsApprovedRequest_PostsUsageLedgerEntry_AndSaves()
    {
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, NationalId, AnnualTypeLabel, "01.09.2026", "05.09.2026", "5", "Geçiş nedeniyle aktarılan izin")]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _requests.Received(1).Add(Arg.Is<Domain.Requests.LeaveRequest>(r =>
            r.EmployeeId == _employeeId && r.Status == Domain.Requests.LeaveRequestStatus.Approved &&
            r.Source == Domain.Requests.LeaveRequestSource.Import && r.CalculatedDays == 5m));
        _ledger.Received(1).AddRange(Arg.Is<IEnumerable<LeaveLedgerEntry>>(entries =>
            entries.Any(e => e.Type == LeaveLedgerEntryType.Usage && e.Amount == -5m && e.Source == LeaveLedgerSource.Import)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_UnmatchedEmployee_ReportsNoMatch()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, "00000000000", Arg.Any<CancellationToken>()).Returns((Guid?)null);
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, "00000000000", AnnualTypeLabel, "01.09.2026", "05.09.2026", "5")]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.NoMatch);
        _requests.DidNotReceive().Add(Arg.Any<Domain.Requests.LeaveRequest>());
    }

    [Fact]
    public async Task ProcessAsync_EmployeeWithoutLeaveProfile_ReportsNoProfile()
    {
        _profiles.FindByEmployeeIdAsync(_employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeLeaveProfile?>(null));
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, NationalId, AnnualTypeLabel, "01.09.2026", "05.09.2026", "5")]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == LeaveDaysImportErrorCodes.NoProfile);
    }

    [Fact]
    public async Task ProcessAsync_UnknownLeaveTypeName_ReportsLookupNotFound()
    {
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, NationalId, "Tanımsız İzin Türü", "01.09.2026", "05.09.2026", "5")]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.LookupNotFound);
    }

    [Fact]
    public async Task ProcessAsync_HourlyLeaveType_ReportsUnsupportedUnit()
    {
        // "Süt izni" (Breastfeeding) is seeded as Hourly - not offered by this first version of the template.
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, NationalId, "Süt izni", "01.09.2026", "01.09.2026", "1")]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == LeaveDaysImportErrorCodes.UnsupportedUnit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("abc")]
    public async Task ProcessAsync_InvalidDays_ReportsInvalidDays(string days)
    {
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [LeaveDaysImportColumns.NationalId],
            [Row(2, NationalId, AnnualTypeLabel, "01.09.2026", "05.09.2026", days)]);

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == LeaveDaysImportErrorCodes.InvalidDays);
    }

    [Fact]
    public async Task ProcessAsync_AllOrNothing_SkipsValidRowsWhenAnyRowFails()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, "00000000000", Arg.Any<CancellationToken>()).Returns((Guid?)null);
        var rows = new[]
        {
            Row(2, NationalId, AnnualTypeLabel, "01.09.2026", "05.09.2026", "5"),
            Row(3, "00000000000", AnnualTypeLabel, "01.09.2026", "05.09.2026", "2"),
        };

        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.AllOrNothing, [LeaveDaysImportColumns.NationalId], rows);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.First(r => r.RowNumber == 2).Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows.First(r => r.RowNumber == 3).Outcome.ShouldBe(ImportRowOutcome.Error);
        _requests.DidNotReceive().Add(Arg.Any<Domain.Requests.LeaveRequest>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
