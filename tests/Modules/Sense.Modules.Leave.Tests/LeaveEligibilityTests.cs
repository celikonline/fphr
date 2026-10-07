using NSubstitute;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Denetim raporu A6: cinsiyete bağlı izin türleri (Babalık=Erkek, Doğum/Süt=Kadın).</summary>
public sealed class LeaveGenderEligibilityTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    // Sabit saat: geriye dönük talep sınırı (LeaveBackdatePolicy) testin sabit tarihlerini gerçek takvime bağlamasın.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Seed_MarksGenderSpecificStatutoryTypes()
    {
        var types = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).ToDictionary(t => t.Code);

        types[LeaveTypeCode.Paternity].EligibleGender.ShouldBe(LeaveEligibleGender.Male);
        types[LeaveTypeCode.Maternity].EligibleGender.ShouldBe(LeaveEligibleGender.Female);
        types[LeaveTypeCode.Breastfeeding].EligibleGender.ShouldBe(LeaveEligibleGender.Female);
        types.Where(t => t.Key is not (LeaveTypeCode.Paternity or LeaveTypeCode.Maternity or LeaveTypeCode.Breastfeeding))
            .ShouldAllBe(t => t.Value.EligibleGender == LeaveEligibleGender.Any);
    }

    [Fact]
    public void EligibleGender_SurvivesTypeUpdateAndSettingsRoundTrip()
    {
        var set = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(Guid.NewGuid(), StatutoryLeaveDurations.Turkey2026);
        var paternity = set.Types.Single(t => t.Code == LeaveTypeCode.Paternity);
        paternity.EligibleGender.ShouldBe(LeaveEligibleGender.Male);
        paternity.ToSettings().EligibleGender.ShouldBe(LeaveEligibleGender.Male);

        set.UpdateType(paternity.Id, paternity.ToSettings() with { EligibleGender = LeaveEligibleGender.Any }).IsSuccess.ShouldBeTrue();

        set.Types.Single(t => t.Code == LeaveTypeCode.Paternity).EligibleGender.ShouldBe(LeaveEligibleGender.Any);
    }

    [Theory]
    [InlineData(LeaveEligibleGender.Any, EmployeeGender.Female, true)]
    [InlineData(LeaveEligibleGender.Any, EmployeeGender.Male, true)]
    [InlineData(LeaveEligibleGender.Male, EmployeeGender.Male, true)]
    [InlineData(LeaveEligibleGender.Male, EmployeeGender.Female, false)]
    [InlineData(LeaveEligibleGender.Female, EmployeeGender.Female, true)]
    [InlineData(LeaveEligibleGender.Female, EmployeeGender.Male, false)]
    public void IsEligible_KnownGender_MustMatch(LeaveEligibleGender required, EmployeeGender gender, bool expected) =>
        LeaveGenderEligibility.IsEligible(required, gender).ShouldBe(expected);

    [Theory]
    [InlineData(LeaveEligibleGender.Male)]
    [InlineData(LeaveEligibleGender.Female)]
    public void IsEligible_UnknownGender_DoesNotRestrict(LeaveEligibleGender required)
    {
        LeaveGenderEligibility.IsEligible(required, null).ShouldBeTrue();
        LeaveGenderEligibility.IsEligible(required, EmployeeGender.Unspecified).ShouldBeTrue();
        LeaveGenderEligibility.IsEligible(required, EmployeeGender.Other).ShouldBeTrue();
    }

    private sealed record Fixture(LeaveRequestRules Rules, LeaveRuleSet Set, Guid TenantId, Guid EmployeeId, IEmployeeLookup Employees, IEmployeeBirthDateLookup BirthDates)
    {
        public Guid TypeId(LeaveTypeCode code) => Set.Types.Single(t => t.Code == code).Id;
    }

    private static Fixture NewFixture(EmployeeGender? gender, bool missingBirthDate = false)
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var set = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
        var profiles = Substitute.For<IEmployeeLeaveProfileRepository>();
        profiles.FindByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>())
            .Returns(EmployeeLeaveProfile.Create(tenantId, employeeId, set.Id, new DateOnly(2020, 1, 1), null));
        var ruleSets = Substitute.For<ILeaveRuleSetRepository>();
        ruleSets.GetByIdAsync(set.Id, Arg.Any<CancellationToken>()).Returns(set);
        var requests = Substitute.For<ILeaveRequestRepository>();
        requests.ListActiveOverlappingAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var days = Substitute.For<ILeaveDayCalculator>();
        days.CalculateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new LeaveDayCount(2m, 0, 0, []));
        var employees = Substitute.For<IEmployeeLookup>();
        employees.GetGenderAsync(Arg.Any<Guid>(), employeeId, Arg.Any<CancellationToken>()).Returns(gender);
        var birthDates = Substitute.For<IEmployeeBirthDateLookup>();
        birthDates.GetBirthDateAsync(Arg.Any<Guid>(), employeeId, Arg.Any<CancellationToken>()).Returns(missingBirthDate ? null : new DateOnly(1990, 10, 5));
        var rules = new LeaveRequestRules(profiles, ruleSets, requests, Substitute.For<ILeaveLedgerRepository>(), days, null!, new LeaveBackdatePolicy(new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero))), employees, Substitute.For<IDocumentAttachmentLookup>(), birthDates);
        return new Fixture(rules, set, tenantId, employeeId, employees, birthDates);
    }

    private static Task<Sense.Shared.Kernel.Results.Result<(LeaveType Type, decimal Days)>> ValidateAsync(Fixture f, LeaveTypeCode code, bool requireSelfService = true) =>
        f.Rules.ValidateAsync(
            Guid.NewGuid(), f.EmployeeId, f.TypeId(code), Start, Start.AddDays(1), "x", null, CancellationToken.None, requireSelfService);

    [Fact]
    public async Task Validate_FemaleEmployee_CannotRequestPaternityLeave()
    {
        var result = await ValidateAsync(NewFixture(EmployeeGender.Female), LeaveTypeCode.Paternity);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveRequestErrors.GenderNotEligible);
    }

    [Fact]
    public async Task Validate_MaleEmployee_CannotRequestMaternityOrBreastfeedingLeave()
    {
        var fixture = NewFixture(EmployeeGender.Male);

        (await ValidateAsync(fixture, LeaveTypeCode.Maternity)).Error.Code.ShouldBe(LeaveRequestErrors.GenderNotEligible);
        (await ValidateAsync(fixture, LeaveTypeCode.Breastfeeding)).Error.Code.ShouldBe(LeaveRequestErrors.GenderNotEligible);
    }

    [Fact]
    public async Task Validate_EligibleGender_PassesTheGenderGate()
    {
        (await ValidateAsync(NewFixture(EmployeeGender.Male), LeaveTypeCode.Paternity)).IsSuccess.ShouldBeTrue();
        (await ValidateAsync(NewFixture(EmployeeGender.Female), LeaveTypeCode.Maternity)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_HrOnBehalf_IsAlsoBoundByGender()
    {
        var result = await ValidateAsync(NewFixture(EmployeeGender.Female), LeaveTypeCode.Paternity, requireSelfService: false);

        result.Error.Code.ShouldBe(LeaveRequestErrors.GenderNotEligible);
    }

    [Fact]
    public async Task Validate_UnknownGender_AppliesNoRestriction()
    {
        (await ValidateAsync(NewFixture(EmployeeGender.Unspecified), LeaveTypeCode.Paternity)).IsSuccess.ShouldBeTrue();
        (await ValidateAsync(NewFixture(null), LeaveTypeCode.Maternity)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_AnyTypes_DoNotLookUpGender()
    {
        var fixture = NewFixture(EmployeeGender.Female);

        (await ValidateAsync(fixture, LeaveTypeCode.Marriage)).IsSuccess.ShouldBeTrue();

        await fixture.Employees.DidNotReceiveWithAnyArgs().GetGenderAsync(default, default, default);
    }

    [Fact]
    public async Task BirthdayLeave_IsRejectedOutsideTheRecordedBirthday()
    {
        var fixture = NewFixture(EmployeeGender.Female);
        var birthday = fixture.Set.Types.Single(t => t.Code == LeaveTypeCode.Birthday);
        fixture.Set.UpdateType(birthday.Id, birthday.ToSettings() with { IsDisabled = false }).IsSuccess.ShouldBeTrue();

        var result = await fixture.Rules.ValidateAsync(Guid.NewGuid(), fixture.EmployeeId, birthday.Id, Start.AddDays(1), Start.AddDays(1), "birthday", null, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveRequestErrors.BirthdayDateOnly);
    }

    [Fact]
    public async Task BirthdayLeave_IsBlockedWhenBirthDateIsMissing()
    {
        var fixture = NewFixture(EmployeeGender.Female, missingBirthDate: true);
        var birthday = fixture.Set.Types.Single(t => t.Code == LeaveTypeCode.Birthday);
        fixture.Set.UpdateType(birthday.Id, birthday.ToSettings() with { IsDisabled = false }).IsSuccess.ShouldBeTrue();

        var result = await fixture.Rules.ValidateAsync(Guid.NewGuid(), fixture.EmployeeId, birthday.Id, Start, Start, "birthday", null, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveRequestErrors.BirthdayBirthDateMissing);
    }

    [Fact]
    public async Task BirthdayLeave_LooksUpBirthDateUsingTheCurrentTenantAndEmployee()
    {
        var fixture = NewFixture(EmployeeGender.Female);
        var birthday = fixture.Set.Types.Single(t => t.Code == LeaveTypeCode.Birthday);
        fixture.Set.UpdateType(birthday.Id, birthday.ToSettings() with { IsDisabled = false }).IsSuccess.ShouldBeTrue();

        await fixture.Rules.ValidateAsync(fixture.TenantId, fixture.EmployeeId, birthday.Id, Start.AddDays(1), Start.AddDays(1), "birthday", null, CancellationToken.None);

        await fixture.BirthDates.Received(1).GetBirthDateAsync(fixture.TenantId, fixture.EmployeeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HourlyLeave_UsesConfiguredThresholdInRequestValidation()
    {
        var fixture = NewFixture(EmployeeGender.Female);

        var result = await fixture.Rules.ValidateAsync(Guid.NewGuid(), fixture.EmployeeId, fixture.TypeId(LeaveTypeCode.JobSearch), Start, Start,
            "job search", null, CancellationToken.None, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(12, 1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Days.ShouldBe(0.5m);
    }
}
