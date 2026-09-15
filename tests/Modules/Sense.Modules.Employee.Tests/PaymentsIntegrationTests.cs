using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// E3 salary history + bank accounts end to end (Dispatcher → handlers → PostgreSQL): permissions (salary.view/edit,
/// bank_accounts.edit, sensitive.view masking), data scope, IBAN encrypted at rest, self-service reads and the
/// SalaryChanged / EmployeeProfileChanged("bank") events through the outbox.
/// </summary>
[Collection("PostgreSQL")]
public sealed class PaymentsIntegrationTests : TestFixture
{
    private const string IbanA = PaymentsDomainTests.IbanA;
    private const string IbanB = PaymentsDomainTests.IbanB;
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly FakeDataScope _scope = new();
    private readonly HashSet<string> _denied = new(StringComparer.Ordinal);
    private readonly RecordingHandler<ContractEvents.SalaryChanged> _salaryChanged = new();
    private readonly RecordingHandler<ContractEvents.EmployeeProfileChanged> _profileChanged = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => !_denied.Contains(call.ArgAt<string>(1)));
        services.AddSingleton(_permissions);
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IUserAccountLookup>(new FakeUserAccountLookup());
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.SalaryChanged>>(_salaryChanged);
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeProfileChanged>>(_profileChanged);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task SalaryEndpoints_AddEditDelete_KeepHistory_AndPublishSalaryChangedOncePerChange()
    {
        var tenantId = SetUser();
        var ali = await CreateAsync("Ali", "Kaya");

        var past = Today.AddYears(-1);
        var added = await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, past, 40_000m, "try", SalaryType.Gross, PayFrequency.Monthly, 1_500m, null, "İlk maaş"));
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : null);
        added.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Currency.ShouldBe("TRY"),
            r => r.IsCurrent.ShouldBeTrue(),
            r => r.TransportAllowance.ShouldBe(1_500m),
            r => r.MealAllowance.ShouldBeNull(),
            r => r.Source.ShouldBe(SalarySource.Manual));

        var future = Today.AddMonths(2);
        var raised = await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, future, 55_000.75m, "TRY", SalaryType.Gross, PayFrequency.Monthly));
        raised.Value.Select(r => (r.EffectiveFrom, r.IsCurrent)).ShouldBe(new (DateOnly, bool)[] { (future, false), (past, true) });

        ShouldFail(await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, past, 1m, "TRY", SalaryType.Net, PayFrequency.Daily)), EmployeeDomainErrors.SalaryEffectiveDateDuplicate);
        ShouldFail(await ExecuteCommandAsync(new DeleteSalaryRecordCommand(ali, Guid.NewGuid())), EmployeeDomainErrors.SalaryRecordNotFound);

        // "Maaş bloğu bütünlüğü": without date + amount nothing else is accepted.
        var invalid = await Should.ThrowAsync<ValidationException>(() =>
            ExecuteCommandAsync(new AddSalaryRecordCommand(ali, null, null, "TRY", SalaryType.Net, PayFrequency.Monthly, 100m)));
        invalid.Errors.Select(e => e.ErrorMessage).ShouldBe([EmployeeDomainErrors.SalaryEffectiveDateRequired, EmployeeDomainErrors.SalaryAmountRequired], ignoreOrder: true);
        var badCurrency = await Should.ThrowAsync<ValidationException>(() =>
            ExecuteCommandAsync(new AddSalaryRecordCommand(ali, Today, 100m, "TL", null, PayFrequency.Monthly)));
        badCurrency.Errors.Select(e => e.ErrorMessage).ShouldBe([EmployeeDomainErrors.CurrencyInvalid, EmployeeDomainErrors.SalaryTypeRequired], ignoreOrder: true);

        var futureId = raised.Value[0].Id;
        (await ExecuteCommandAsync(new UpdateSalaryRecordCommand(ali, futureId, future, 55_000.75m, "TRY", SalaryType.Gross, PayFrequency.Monthly))).IsSuccess.ShouldBeTrue();
        var moved = Today.AddMonths(1);
        var edited = await ExecuteCommandAsync(new UpdateSalaryRecordCommand(ali, futureId, moved, 56_000m, "TRY", SalaryType.Net, PayFrequency.Monthly, null, 900m));
        edited.Value[0].ShouldSatisfyAllConditions(r => r.EffectiveFrom.ShouldBe(moved), r => r.Amount.ShouldBe(56_000m), r => r.SalaryType.ShouldBe(SalaryType.Net));

        var deleted = await ExecuteCommandAsync(new DeleteSalaryRecordCommand(ali, futureId));
        deleted.Value.ShouldHaveSingleItem().EffectiveFrom.ShouldBe(past);

        // Salary data never rides on the employee card.
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali))).IsSuccess.ShouldBeTrue();

        (await CountAsync<ContractEvents.SalaryChanged>(tenantId)).ShouldBe(4);
        await ProcessOutboxAsync();
        _salaryChanged.Received.Where(e => e.EmployeeId == ali).Select(e => (e.SalaryRecordId == futureId, e.EffectiveFrom)).ShouldBe(new (bool, DateOnly)[]
        {
            (false, past),
            (true, future),
            (true, moved),
            (true, moved),
        });
        _salaryChanged.Received.ShouldAllBe(e => e.TenantId == tenantId);
    }

    [Fact]
    public async Task SalaryPermissions_ViewAndEditAreSeparate_AndScopeApplies()
    {
        SetUser();
        var ali = await CreateAsync("Ali", "Kaya");
        var outsider = await CreateAsync("Oğuz", "Dış");
        (await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, Today, 40_000m, "TRY", SalaryType.Gross, PayFrequency.Monthly))).IsSuccess.ShouldBeTrue();

        _denied.Add(EmployeePermissions.SalaryViewKey);
        ShouldFail(await ExecuteQueryAsync(new GetEmployeeSalariesQuery(ali)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFail(await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, Today.AddDays(1), 1m, "TRY", SalaryType.Gross, PayFrequency.Monthly)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        // Bank accounts are readable without salary.view.
        (await ExecuteQueryAsync(new GetEmployeeBankAccountsQuery(ali))).IsSuccess.ShouldBeTrue();

        _denied.Clear();
        _denied.Add(EmployeePermissions.SalaryEditKey);
        (await ExecuteQueryAsync(new GetEmployeeSalariesQuery(ali))).Value.ShouldHaveSingleItem();
        ShouldFail(await ExecuteCommandAsync(new DeleteSalaryRecordCommand(ali, Guid.NewGuid())), ErrorCodes.Forbidden, ErrorType.Forbidden);
        _denied.Add(EmployeePermissions.BankAccountsEditKey);
        ShouldFail(await ExecuteCommandAsync(new AddBankAccountCommand(ali, IbanA, BankAccountType.DemandDeposit)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        // Unit manager: only employees inside the authorized units.
        _denied.Clear();
        _scope.Visible = new HashSet<Guid> { ali };
        (await ExecuteQueryAsync(new GetEmployeeSalariesQuery(ali))).IsSuccess.ShouldBeTrue();
        ShouldFail(await ExecuteQueryAsync(new GetEmployeeSalariesQuery(outsider)), ErrorCodes.OutOfScope, ErrorType.NotFound);
        ShouldFail(await ExecuteCommandAsync(new AddSalaryRecordCommand(outsider, Today, 1m, "TRY", SalaryType.Gross, PayFrequency.Monthly)), ErrorCodes.OutOfScope, ErrorType.NotFound);
        ShouldFail(await ExecuteQueryAsync(new GetEmployeeBankAccountsQuery(outsider)), ErrorCodes.OutOfScope, ErrorType.NotFound);
        ShouldFail(await ExecuteCommandAsync(new AddBankAccountCommand(outsider, IbanA, BankAccountType.DemandDeposit)), ErrorCodes.OutOfScope, ErrorType.NotFound);
    }

    [Fact]
    public async Task BankAccounts_IbanEncryptedAtRest_MaskedWithoutSensitiveView_AndPrimaryInvariantHolds()
    {
        var tenantId = SetUser();
        var ali = await CreateAsync("Ali", "Kaya");

        var first = await ExecuteCommandAsync(new AddBankAccountCommand(ali, "tr33 0006 1005 1978 6457 8413 26", BankAccountType.DemandDeposit, "Ziraat", "try"));
        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Code : null);
        first.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.Iban.ShouldBe(IbanA),
            a => a.IsIbanMasked.ShouldBeFalse(),
            a => a.IsPrimary.ShouldBeTrue(),
            a => a.Currency.ShouldBe("TRY"));

        var second = await ExecuteCommandAsync(new AddBankAccountCommand(ali, IbanB, BankAccountType.TimeDeposit, IsPrimary: true));
        second.Value.Select(a => (a.Iban, a.IsPrimary)).ShouldBe(new (string, bool)[] { ("TR13 0006 2000 0000 1234 5678 90", true), (IbanA, false) });
        ShouldFail(await ExecuteCommandAsync(new AddBankAccountCommand(ali, IbanA.Replace(" ", string.Empty, StringComparison.Ordinal), BankAccountType.Other)), EmployeeDomainErrors.IbanDuplicate);
        var foreign = await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new AddBankAccountCommand(ali, "GB82 WEST 1234 5698 7654 32", BankAccountType.Other)));
        foreign.Errors.Single().ErrorMessage.ShouldBe(EmployeeDomainErrors.IbanCountryUnsupported);

        // At rest: only ciphertext and the mask; the ciphertext decrypts back to the normalized IBAN.
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var stored = await db.Set<BankAccount>().AsNoTracking().Where(a => a.EmployeeId == ali).ToListAsync();
        stored.ShouldAllBe(a => !a.ProtectedIban.Contains("0006100519786457", StringComparison.Ordinal) && !a.ProtectedIban.Contains("0006200000001234", StringComparison.Ordinal));
        var encryption = ServiceProvider!.GetRequiredService<IEncryptionProvider>();
        stored.Select(a => encryption.Decrypt(a.ProtectedIban)).ShouldBe(["TR330006100519786457841326", "TR130006200000001234567890"], ignoreOrder: true);
        stored.Select(a => a.MaskedIban).ShouldBe(["TR** **** **** **** **** **13 26", "TR** **** **** **** **** **78 90"], ignoreOrder: true);

        // Without employee.sensitive.view: masked on read, and an edit without an IBAN keeps the stored one.
        _denied.Add(EmployeePermissions.SensitiveViewKey);
        var masked = (await ExecuteQueryAsync(new GetEmployeeBankAccountsQuery(ali))).Value;
        masked.ShouldAllBe(a => a.IsIbanMasked);
        masked.Select(a => a.Iban).ShouldBe(["TR** **** **** **** **** **78 90", "TR** **** **** **** **** **13 26"]);
        var primaryId = masked[0].Id;
        var edited = await ExecuteCommandAsync(new UpdateBankAccountCommand(ali, primaryId, null, BankAccountType.Other, "Garanti", null, "Maaş hesabı"));
        edited.Value[0].ShouldSatisfyAllConditions(a => a.Iban.ShouldBe("TR** **** **** **** **** **78 90"), a => a.BankName.ShouldBe("Garanti"));
        _denied.Clear();

        var otherId = masked[1].Id;
        (await ExecuteCommandAsync(new SetPrimaryBankAccountCommand(ali, otherId))).Value[0].Id.ShouldBe(otherId);
        (await ExecuteCommandAsync(new SetPrimaryBankAccountCommand(ali, otherId))).IsSuccess.ShouldBeTrue();
        var afterDelete = await ExecuteCommandAsync(new DeleteBankAccountCommand(ali, otherId));
        afterDelete.Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(a => a.Id.ShouldBe(primaryId), a => a.IsPrimary.ShouldBeTrue());

        // add, add, edit, set primary, delete (the no-op set primary publishes nothing).
        (await CountAsync<ContractEvents.EmployeeProfileChanged>(tenantId)).ShouldBe(5);
        (await CountAsync<ContractEvents.SalaryChanged>(tenantId)).ShouldBe(0);
        await ProcessOutboxAsync();
        _profileChanged.Received.ShouldAllBe(e => e.EmployeeId == ali && e.Section == ContractEvents.EmployeeProfileSections.Bank);
    }

    [Fact]
    public async Task Me_ReadsOwnSalaryAndUnmaskedBankAccounts_WithoutSalaryOrSensitivePermissions()
    {
        SetUser();
        var ali = await CreateAsync("Ali", "Kaya");
        var other = await CreateAsync("Oğuz", "Dış");
        (await ExecuteCommandAsync(new AddSalaryRecordCommand(ali, Today, 40_000m, "TRY", SalaryType.Gross, PayFrequency.Monthly))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new AddSalaryRecordCommand(other, Today, 90_000m, "TRY", SalaryType.Gross, PayFrequency.Monthly))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new AddBankAccountCommand(ali, IbanA, BankAccountType.DemandDeposit))).IsSuccess.ShouldBeTrue();

        SetUser(employeeId: ali, tenantId: TenantContextAccessor!.TenantId);
        _denied.UnionWith([EmployeePermissions.SalaryViewKey, EmployeePermissions.SensitiveViewKey, EmployeePermissions.EmployeesReadKey]);

        (await ExecuteQueryAsync(new GetMySalariesQuery())).Value.ShouldHaveSingleItem().Amount.ShouldBe(40_000m);
        (await ExecuteQueryAsync(new GetMyBankAccountsQuery())).Value.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.Iban.ShouldBe(IbanA), a => a.IsIbanMasked.ShouldBeFalse());
        ShouldFail(await ExecuteQueryAsync(new GetEmployeeSalariesQuery(other)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        SetUser(employeeId: null, tenantId: TenantContextAccessor!.TenantId);
        ShouldFail(await ExecuteQueryAsync(new GetMySalariesQuery()), EmployeeErrors.EmployeeNotFound, ErrorType.NotFound);
    }

    private async Task<Guid> CreateAsync(string firstName, string lastName)
    {
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand(firstName, lastName, Tckn.Next(), null, null, null, new DateOnly(2024, 1, 1), null, null, null));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value.Id;
    }

    private async Task<int> CountAsync<TEvent>(Guid tenantId)
    {
        var type = EventTypeRegistry.NameOf(typeof(TEvent));
        return await ServiceProvider!.GetRequiredService<EmployeeDbContext>().OutboxMessages.AsNoTracking()
            .CountAsync(m => m.TenantId == tenantId && m.Type == type);
    }

    private async Task ProcessOutboxAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
    }

    private static void ShouldFail<T>(Result<T> result, string code, ErrorType? type = null)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        if (type is { } expected)
        {
            result.Error.Type.ShouldBe(expected);
        }
    }

    private Guid SetUser(Guid? employeeId = null, Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        _denied.Clear();
        _scope.Visible = null;
        _scope.AccessibleOrgUnits = null;
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: employeeId, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        if (tenantId is null)
        {
            TenantContextSetter!.BeginScope(tenant);
        }

        return tenant;
    }
}
