using System.Reflection;
using NSubstitute;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>Incentive definition handlers (PAY-INCENTIVE-2): create, template, approve/revoke, update, activate and the read models. Repositories are in-memory fakes.</summary>
public sealed class IncentiveDefinitionFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeIncentiveDefinitionRepository _definitions = new();
    private readonly FakeIncentiveAssignmentRepository _assignments = new();
    private readonly IPayrollPeriodRepository _periods = Substitute.For<IPayrollPeriodRepository>();
    private readonly IPayslipDirtyFlagRepository _flags = Substitute.For<IPayslipDirtyFlagRepository>();
    private readonly ILegislationParameterReader _legislation = Substitute.For<ILegislationParameterReader>();
    private readonly IUserProfileLookup _users = Substitute.For<IUserProfileLookup>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly TimeProvider _clock = Substitute.For<TimeProvider>();
    private readonly IncentiveChangeFlagger _flagger;

    public IncentiveDefinitionFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _currentUser.UserId.Returns(UserId);
        _clock.GetUtcNow().Returns(Now);
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _users.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<Guid, UserProfileInfo>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).ToDictionary(id => id, id => new UserProfileInfo(id, null, "smmm@example.com", "Mali Müşavir", null, true))));
        _legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<LegislationParameterValue>>(
                call.ArgAt<IReadOnlyCollection<string>>(0)
                    .Select(key => new LegislationParameterValue(key, "TR", 0.05m, null, new DateOnly(2026, 1, 1), null, "test"))
                    .ToList()));
        _flagger = new IncentiveChangeFlagger(_tenant, _periods, _assignments, new PayslipDirtyFlagService(_periods, _flags, _clock));
    }

    private static IncentiveDefinitionInput Input(
        IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount,
        IncentiveParametersDto? parameters = null,
        string name = "Test teşviki",
        string? declarationCode = "06111") =>
        new(name, "Test Kanunu", declarationCode, kind, parameters ?? new IncentiveParametersDto(new IncentiveRateDto(0.05m, null), null, null, null, false, false, false, false), null, new DateOnly(2026, 1, 1), null);

    private IncentiveDefinition Stored(string code = "TEST", IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount)
    {
        var definition = IncentiveDefinition.Create(TenantId, code, Input(kind).ToDataForTest()).Value;
        _definitions.Add(definition);
        return definition;
    }

    private PayrollPeriod OpenPeriod(int year, int month)
    {
        var period = PayrollPeriod.Create(TenantId, year, month).Value;
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<PayrollPeriod>>(
                new[] { period }.Where(p => p.Year > call.Arg<DateOnly>().Year || (p.Year == call.Arg<DateOnly>().Year && p.Month >= call.Arg<DateOnly>().Month)).ToList()));
        return period;
    }

    private void Assigned(IncentiveDefinition definition, Guid employeeId, DateOnly from, DateOnly? to)
    {
        var assignment = EmployeeIncentiveAssignment.Create(
            TenantId, employeeId, definition, from, to, null, new HashSet<DateOnly>(), [], _ => definition.Category).Value;
        _assignments.Add(assignment);
    }

    [Fact]
    public async Task Create_StoresADraftThatIsNotApproved()
    {
        var handler = new CreateIncentiveDefinitionHandler(_tenant, _definitions, _clock);

        var result = await handler.Handle(new CreateIncentiveDefinitionCommand("TEST", Input()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = _definitions.Items.Single();
        stored.Code.ShouldBe("TEST");
        stored.TenantId.ShouldBe(TenantId);
        stored.IsSmmmApproved.ShouldBeFalse();
        stored.Parameters.Rate!.Value.ShouldBe(0.05m);
    }

    [Fact]
    public async Task Create_WithoutValidFrom_StartsToday()
    {
        var handler = new CreateIncentiveDefinitionHandler(_tenant, _definitions, _clock);

        await handler.Handle(new CreateIncentiveDefinitionCommand("TEST", Input() with { ValidFrom = null }), CancellationToken.None);

        _definitions.Items.Single().ValidFrom.ShouldBe(new DateOnly(2026, 6, 10));
    }

    [Fact]
    public async Task Create_WithATakenCode_IsAConflict()
    {
        Stored("TEST");
        var handler = new CreateIncentiveDefinitionHandler(_tenant, _definitions, _clock);

        var result = await handler.Handle(new CreateIncentiveDefinitionCommand("TEST", Input()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveCodeTaken);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _definitions.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Create_WithAnInvalidCode_IsRefused()
    {
        var handler = new CreateIncentiveDefinitionHandler(_tenant, _definitions, _clock);

        (await handler.Handle(new CreateIncentiveDefinitionCommand("bad code", Input()), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveCodeInvalid);
        _definitions.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task FromTemplate_5510_IsCreatedApprovedBySystem()
    {
        var handler = new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock);

        var result = await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = _definitions.Items.Single();
        stored.Code.ShouldBe("SGK_5510_MD81");
        stored.TemplateKey.ShouldBe(IncentiveTemplates.Tr5510Md81);
        stored.DeclarationCode.ShouldBe("05510");
        stored.IsSmmmApproved.ShouldBeTrue();
        stored.ApprovedByUserId.ShouldBeNull();
        stored.ApprovedAt.ShouldBe(Now);
        stored.ApprovalNote.ShouldBe(IncentiveTemplates.SystemApprovalNote);
        stored.Parameters.Rate!.LegislationKey.ShouldBe("Payroll.Sgk.EmployerDiscountRate");
    }

    [Fact]
    public async Task FromTemplate_6111_IsACompleteModelAwaitingApproval()
    {
        var handler = new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock);

        await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr6111), CancellationToken.None);

        var stored = _definitions.Items.Single();
        stored.IsSmmmApproved.ShouldBeFalse();
        stored.IsComplete.ShouldBeTrue();
        stored.Parameters.Rate!.Value.ShouldBe(1m);
        stored.DeclarationCode.ShouldBe("06111");
        stored.Parameters.EmployerSgkRate!.LegislationKey.ShouldBe(IncentiveTemplates.EmployerSgkRateKey);
    }

    [Fact]
    public async Task ApiParameters_KeepTheTotalSupportCapOnCreateAndRead()
    {
        var parameters = new IncentiveParametersDto(new IncentiveRateDto(1m, null), IncentiveCoverageBase.ActualSgkBase, null,
            new IncentiveRateDto(0.205m, null), true, true, true, true, MaxSupportMultiplier: 1m);
        var input = new IncentiveDefinitionInput("Tavanlı teşvik", null, "17103", IncentiveKind.EmployerShareCoverage,
            parameters, null, new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31));
        var created = await new CreateIncentiveDefinitionHandler(_tenant, _definitions, _clock)
            .Handle(new CreateIncentiveDefinitionCommand("TAVANLI", input), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();
        _definitions.Items.Single().Parameters.MaxSupportMultiplier.ShouldBe(1m);
        var read = await new GetIncentiveDefinitionHandler(_tenant, _definitions, _users)
            .Handle(new GetIncentiveDefinitionQuery(created.Value), CancellationToken.None);
        read.Value.Parameters.MaxSupportMultiplier.ShouldBe(1m);
    }

    [Fact]
    public async Task FromTemplate_TwiceForTheSameTemplate_IsAConflict()
    {
        var handler = new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock);
        (await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var second = await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None);

        second.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveTemplateAlreadyAdded);
        second.Error.Type.ShouldBe(ErrorType.Conflict);
        _definitions.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task FromTemplate_WhenTheCodeIsAlreadyTakenManually_IsAConflict()
    {
        Stored("SGK_5510_MD81");
        var handler = new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock);

        var result = await handler.Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr5510Md81), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveCodeTaken);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task FromTemplate_Unknown_IsNotFound()
    {
        var handler = new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock);

        var result = await handler.Handle(new CreateIncentiveFromTemplateCommand("NOPE"), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveTemplateNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Templates_ShowWhichOnesWereAlreadyAdded()
    {
        await new CreateIncentiveFromTemplateHandler(_tenant, _definitions, _clock)
            .Handle(new CreateIncentiveFromTemplateCommand(IncentiveTemplates.Tr6111), CancellationToken.None);

        var list = (await new ListIncentiveTemplatesHandler(_definitions).Handle(new ListIncentiveTemplatesQuery(), CancellationToken.None)).Value;

        list.Count.ShouldBe(IncentiveTemplates.All.Count);
        list.Single(t => t.TemplateKey == IncentiveTemplates.Tr6111).AlreadyAdded.ShouldBeTrue();
        list.Where(t => t.TemplateKey != IncentiveTemplates.Tr6111).ShouldAllBe(t => !t.AlreadyAdded);
        list.Single(t => t.TemplateKey == IncentiveTemplates.Tr5510Md81).PreApproved.ShouldBeTrue();
    }

    [Fact]
    public async Task ParameterCatalog_ListsOnlyTheAllowedKeysInForceToday()
    {
        _clock.GetUtcNow().Returns(Now);
        _legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<LegislationParameterValue>>(
            [
                new("Payroll.Sgk.EmployerRate", "TR", 0.155m, null, new DateOnly(2026, 1, 1), null, "t"),
                new("Payroll.Sgk.EmployeeRate", "TR", 0.14m, null, new DateOnly(2020, 1, 1), new DateOnly(2025, 12, 31), "t"),
                new("Payroll.Sgk.EmployeeRate", "TR", 0.15m, null, new DateOnly(2026, 1, 1), null, "t"),
                new("Payroll.MinimumWage.Gross", "TR", 33030m, null, new DateOnly(2026, 1, 1), null, "t"),
                new("Payroll.Sgk.Floor", "TR", 1m, null, new DateOnly(2027, 1, 1), null, "t"),
            ]));

        var catalog = (await new GetIncentiveParameterCatalogHandler(_legislation, _clock)
            .Handle(new GetIncentiveParameterCatalogQuery(), CancellationToken.None)).Value;

        await _legislation.Received(1).ListAsync(
            Arg.Is<IReadOnlyCollection<string>>(keys => keys.All(k => k.StartsWith("Payroll.Sgk.") || k.StartsWith("Payroll.Unemployment.") || k == "Payroll.MinimumWage.Gross")),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        catalog.LegislationKeys.Select(k => k.Key).ShouldBe(["Payroll.Sgk.EmployeeRate", "Payroll.Sgk.EmployerRate", "Payroll.MinimumWage.Gross"], ignoreOrder: true);
        catalog.LegislationKeys.Single(k => k.Key == "Payroll.Sgk.EmployeeRate").Value.ShouldBe(0.15m);
        catalog.LegislationKeys.Single(k => k.Key == "Payroll.Sgk.EmployeeRate").EffectiveFrom.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task Approve_RecordsWhoWhenAndTheNote_AndFlagsTheAssignedEmployees()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        var result = await handler.Handle(new ApproveIncentiveDefinitionCommand(definition.Id, "SMMM görüşü alındı"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.IsSmmmApproved.ShouldBeTrue();
        definition.ApprovedByUserId.ShouldBe(UserId);
        definition.ApprovedAt.ShouldBe(Now);
        definition.ApprovalNote.ShouldBe("SMMM görüşü alındı");
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA && f.Reasons.Contains(PayslipDirtyFlagReasons.IncentiveChanged)));
    }

    [Fact]
    public async Task Approve_AnIncompleteDefinition_ReportsTheMissingField()
    {
        var definition = IncentiveDefinition.Create(TenantId, "DRAFT", Input(parameters: new IncentiveParametersDto(null, null, null, null, false, false, false, false)).ToDataForTest()).Value;
        _definitions.Add(definition);
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        var result = await handler.Handle(new ApproveIncentiveDefinitionCommand(definition.Id, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersIncomplete);
        result.Error.Args!["field"].ShouldBe("rate");
        definition.IsSmmmApproved.ShouldBeFalse();
        await _legislation.DidNotReceiveWithAnyArgs().ListAsync(default!, default!, default);
    }

    [Fact]
    public async Task Approve_WhenALegislationKeyDoesNotExist_IsRefused()
    {
        var definition = IncentiveDefinition.Create(
            TenantId, "KEYED", Input(parameters: new IncentiveParametersDto(new IncentiveRateDto(null, "Payroll.Sgk.Nonexistent"), null, null, null, false, false, false, false)).ToDataForTest()).Value;
        _definitions.Add(definition);
        _legislation.ListAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LegislationParameterValue>>([]));
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        var result = await handler.Handle(new ApproveIncentiveDefinitionCommand(definition.Id, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveLegislationKeyInvalid);
        result.Error.Args!["field"].ShouldBe("rate");
        definition.IsSmmmApproved.ShouldBeFalse();
    }

    [Fact]
    public async Task Approve_WithAnExistingLegislationKey_Succeeds()
    {
        var definition = IncentiveDefinition.Create(
            TenantId, "KEYED", Input(parameters: new IncentiveParametersDto(new IncentiveRateDto(null, "Payroll.Sgk.EmployerDiscountRate"), null, null, null, false, false, false, false)).ToDataForTest()).Value;
        _definitions.Add(definition);
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        (await handler.Handle(new ApproveIncentiveDefinitionCommand(definition.Id, null), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeTrue();
        await _legislation.Received(1).ListAsync(
            Arg.Is<IReadOnlyCollection<string>>(keys => keys.SequenceEqual(new[] { "Payroll.Sgk.EmployerDiscountRate" })), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_ASgkDefinitionWithoutDeclarationCode_IsRefused()
    {
        var definition = IncentiveDefinition.Create(TenantId, "NOCODE", Input(declarationCode: null).ToDataForTest()).Value;
        _definitions.Add(definition);
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        (await handler.Handle(new ApproveIncentiveDefinitionCommand(definition.Id, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
    }

    [Fact]
    public async Task Approve_OfAnUnknownDefinition_IsNotFound()
    {
        var handler = new ApproveIncentiveDefinitionHandler(_definitions, _legislation, _currentUser, _flagger, _clock);

        (await handler.Handle(new ApproveIncentiveDefinitionCommand(Guid.NewGuid(), null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveNotFound);
    }

    [Fact]
    public async Task Revoke_ClearsTheApproval_KeepsTheNote_AndFlagsTheAssignedEmployees()
    {
        var definition = Stored();
        definition.Approve(UserId, Now, null);
        OpenPeriod(2026, 6);
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        var handler = new RevokeIncentiveApprovalHandler(_definitions, _flagger);

        var result = await handler.Handle(new RevokeIncentiveApprovalCommand(definition.Id, "Görüş değişti"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.IsSmmmApproved.ShouldBeFalse();
        definition.ApprovedByUserId.ShouldBeNull();
        definition.ApprovalNote.ShouldBe("Görüş değişti");
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    [Fact]
    public async Task Update_OfACalculationField_DropsTheApproval()
    {
        var definition = Stored();
        definition.Approve(UserId, Now, "ok");
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var changedRate = new IncentiveParametersDto(new IncentiveRateDto(0.10m, null), null, null, null, false, false, false, false);
        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(parameters: changedRate)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.IsSmmmApproved.ShouldBeFalse();
        definition.Parameters.Rate!.Value.ShouldBe(0.10m);
    }

    [Fact]
    public async Task Update_OfTheNameOnly_KeepsTheApproval()
    {
        var definition = Stored();
        definition.Approve(UserId, Now, "ok");
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(name: "Yeni ad")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Name.ShouldBe("Yeni ad");
        definition.IsSmmmApproved.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_FlagsOnlyEmployeesAssignedInOpenPeriods()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);
        var employeeB = Guid.NewGuid();
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        Assigned(definition, employeeB, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1));
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(name: "Yeni ad")), CancellationToken.None);

        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
        _flags.DidNotReceive().Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == employeeB));
    }

    [Fact]
    public async Task Update_ChangingTheCategoryOfAnAssignedDefinition_IsAConflict()
    {
        var definition = Stored();
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(IncentiveKind.IncomeTaxWithholdingRelief)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveKindCategoryLocked);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        definition.Kind.ShouldBe(IncentiveKind.EmployerSgkRateDiscount);
    }

    [Fact]
    public async Task Update_RemovingTheDeclarationCodeOfAnAssignedSgkDefinition_IsRefused()
    {
        var definition = Stored();
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(declarationCode: " ")), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
        definition.DeclarationCode.ShouldBe("06111");
    }

    [Fact]
    public async Task Update_ChangingKindWithinTheCategoryOfAnAssignedDefinition_IsAllowed()
    {
        var definition = Stored();
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(IncentiveKind.EmployerShareCoverage)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Kind.ShouldBe(IncentiveKind.EmployerShareCoverage);
    }

    [Fact]
    public async Task Update_OfADefinitionWithoutAssignments_MayChangeCategoryAndDropTheCode()
    {
        var definition = Stored();
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveDefinitionCommand(definition.Id, Input(IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Category.ShouldBe(IncentiveCategory.Tax);
        definition.DeclarationCode.ShouldBeNull();
    }

    [Fact]
    public async Task Update_OfAnUnknownDefinition_IsNotFound()
    {
        var handler = new UpdateIncentiveDefinitionHandler(_definitions, _assignments, _flagger);

        (await handler.Handle(new UpdateIncentiveDefinitionCommand(Guid.NewGuid(), Input()), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveNotFound);
    }

    [Fact]
    public async Task Deactivate_ThenActivate_FlagsOnlyOnARealChange()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);
        Assigned(definition, EmployeeA, new DateOnly(2026, 1, 1), null);

        await new ActivateIncentiveDefinitionHandler(_definitions, _flagger).Handle(new ActivateIncentiveDefinitionCommand(definition.Id), CancellationToken.None);
        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());

        await new DeactivateIncentiveDefinitionHandler(_definitions, _flagger).Handle(new DeactivateIncentiveDefinitionCommand(definition.Id), CancellationToken.None);
        definition.IsActive.ShouldBeFalse();
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    [Fact]
    public async Task Get_And_List_ShowTheApproversName()
    {
        var definition = Stored();
        definition.Approve(UserId, Now, null);
        var inactive = Stored("OLD");
        inactive.Deactivate();

        var one = (await new GetIncentiveDefinitionHandler(_tenant, _definitions, _users).Handle(new GetIncentiveDefinitionQuery(definition.Id), CancellationToken.None)).Value;
        var active = (await new ListIncentiveDefinitionsHandler(_tenant, _definitions, _users).Handle(new ListIncentiveDefinitionsQuery(), CancellationToken.None)).Value;
        var all = (await new ListIncentiveDefinitionsHandler(_tenant, _definitions, _users).Handle(new ListIncentiveDefinitionsQuery(true), CancellationToken.None)).Value;

        one.ApprovedByName.ShouldBe("Mali Müşavir");
        one.ApprovedByUserId.ShouldBe(UserId);
        one.Category.ShouldBe(IncentiveCategory.Sgk);
        one.IsComplete.ShouldBeTrue();
        one.Parameters.Rate!.Value.ShouldBe(0.05m);
        active.Select(d => d.Code).ShouldBe(["TEST"]);
        all.Select(d => d.Code).ShouldBe(["OLD", "TEST"]);
    }

    [Fact]
    public async Task Get_OfAnUnknownDefinition_IsNotFound()
    {
        var result = await new GetIncentiveDefinitionHandler(_tenant, _definitions, _users).Handle(new GetIncentiveDefinitionQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData(typeof(ListIncentiveDefinitionsQuery), "payroll.trace.view")]
    [InlineData(typeof(GetIncentiveDefinitionQuery), "payroll.trace.view")]
    [InlineData(typeof(ListIncentiveTemplatesQuery), "payroll.trace.view")]
    [InlineData(typeof(GetIncentiveParameterCatalogQuery), "payroll.trace.view")]
    [InlineData(typeof(ListIncentiveAssignmentsQuery), "payroll.trace.view")]
    [InlineData(typeof(CreateIncentiveDefinitionCommand), "payroll.incentive.manage")]
    [InlineData(typeof(CreateIncentiveFromTemplateCommand), "payroll.incentive.manage")]
    [InlineData(typeof(UpdateIncentiveDefinitionCommand), "payroll.incentive.manage")]
    [InlineData(typeof(ActivateIncentiveDefinitionCommand), "payroll.incentive.manage")]
    [InlineData(typeof(DeactivateIncentiveDefinitionCommand), "payroll.incentive.manage")]
    [InlineData(typeof(ApproveIncentiveDefinitionCommand), "payroll.incentive.approve")]
    [InlineData(typeof(RevokeIncentiveApprovalCommand), "payroll.incentive.approve")]
    [InlineData(typeof(CreateIncentiveAssignmentsCommand), "payroll.incentive.manage")]
    [InlineData(typeof(UpdateIncentiveAssignmentCommand), "payroll.incentive.manage")]
    [InlineData(typeof(DeleteIncentiveAssignmentCommand), "payroll.incentive.manage")]
    [InlineData(typeof(ListLegacyIncentiveCodesQuery), "payroll.incentive.manage")]
    [InlineData(typeof(ConvertLegacyIncentiveCodeCommand), "payroll.incentive.manage")]
    public void EveryRequest_DeclaresItsPermission(Type request, string permission)
    {
        var attribute = request.GetCustomAttribute<RequiresPermissionAttribute>();

        attribute.ShouldNotBeNull();
        attribute.Permission.ShouldBe(permission);
    }

    [Fact]
    public void ThePermissions_AreRegisteredWithPayroll()
    {
        PayrollPermissions.All.Select(p => p.Key).ShouldContain("payroll.incentive.manage");
        PayrollPermissions.All.Select(p => p.Key).ShouldContain("payroll.incentive.approve");
    }

    [Fact]
    public async Task Approve_WithOnlyTheManagePermission_IsForbidden_AndTheHandlerIsNotReached()
    {
        var reached = false;
        var result = await ForbiddenResult<ApproveIncentiveDefinitionCommand, Result>(
            new ApproveIncentiveDefinitionCommand(Guid.NewGuid(), null), "payroll.incentive.manage", () =>
            {
                reached = true;
                return Task.FromResult(Result.Success());
            });

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["permission"].ShouldBe("payroll.incentive.approve");
    }

    [Fact]
    public async Task Create_WithOnlyTheReadPermission_IsForbidden_AndTheHandlerIsNotReached()
    {
        var reached = false;
        var result = await ForbiddenResult<CreateIncentiveDefinitionCommand, Result<Guid>>(
            new CreateIncentiveDefinitionCommand("TEST", Input()), "payroll.trace.view", () =>
            {
                reached = true;
                return Task.FromResult(Result.Success(Guid.NewGuid()));
            });

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["permission"].ShouldBe("payroll.incentive.manage");
    }

    [Fact]
    public async Task Assign_WithoutThePermission_IsForbidden_AndTheHandlerIsNotReached()
    {
        var reached = false;
        var result = await ForbiddenResult<CreateIncentiveAssignmentsCommand, Result<CreateIncentiveAssignmentsResult>>(
            new CreateIncentiveAssignmentsCommand(Guid.NewGuid(), [EmployeeA], 2026, 6, null, null, null), "payroll.incentive.approve", () =>
            {
                reached = true;
                return Task.FromResult(Result.Success(new CreateIncentiveAssignmentsResult([])));
            });

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["permission"].ShouldBe("payroll.incentive.manage");
    }

    [Fact]
    public async Task Convert_WithoutThePermission_IsForbidden_AndTheHandlerIsNotReached()
    {
        var reached = false;
        var result = await ForbiddenResult<ConvertLegacyIncentiveCodeCommand, Result<ConvertLegacyIncentiveCodeResult>>(
            new ConvertLegacyIncentiveCodeCommand("5510", Guid.NewGuid(), 2026, 6, null, null), "payroll.trace.view", () =>
            {
                reached = true;
                return Task.FromResult(Result.Success(new ConvertLegacyIncentiveCodeResult([], [])));
            });

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["permission"].ShouldBe("payroll.incentive.manage");
    }

    private static async Task<TResponse> ForbiddenResult<TRequest, TResponse>(TRequest request, string grantedPermission, Func<Task<TResponse>> next)
        where TRequest : notnull
        where TResponse : Result
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<string>(1) == grantedPermission);
        var behaviour = new AuthorizationBehaviour<TRequest, TResponse>(user, permissions, Substitute.For<IDataScopeService>());

        return await behaviour.Handle(request, () => next(), CancellationToken.None);
    }
}

internal static class IncentiveInputTestExtensions
{
    public static IncentiveDefinitionData ToDataForTest(this IncentiveDefinitionInput input) => new(
        input.Name, input.LawReference, input.DeclarationCode, input.Kind, input.Parameters?.ToDomainForTest() ?? IncentiveParameters.Empty,
        input.MaxMonths, input.ValidFrom ?? new DateOnly(2026, 1, 1), input.ValidTo);

    private static IncentiveParameters ToDomainForTest(this IncentiveParametersDto dto) => new()
    {
        Rate = dto.Rate is null ? null : new IncentiveRate { Value = dto.Rate.Value, LegislationKey = dto.Rate.LegislationKey },
        CoverageBase = dto.CoverageBase,
        CapMultiplier = dto.CapMultiplier,
        EmployerSgkRate = dto.EmployerSgkRate is null ? null : new IncentiveRate { Value = dto.EmployerSgkRate.Value, LegislationKey = dto.EmployerSgkRate.LegislationKey },
        CoversEmployerSgk = dto.CoversEmployerSgk,
        CoversEmployerUnemployment = dto.CoversEmployerUnemployment,
        CoversEmployeeSgk = dto.CoversEmployeeSgk,
        CoversEmployeeUnemployment = dto.CoversEmployeeUnemployment,
    };
}
