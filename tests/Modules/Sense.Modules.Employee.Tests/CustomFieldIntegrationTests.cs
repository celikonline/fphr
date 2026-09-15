using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.CustomFields;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// E4 custom fields end to end (Dispatcher → handlers → PostgreSQL): definition catalog CRUD/permissions/immutability,
/// the HR write path (any active definition, sensitive values preserved without employee.sensitive.view) versus the
/// self-service path (own record only, only IsEmployeeEditable definitions), and card visibility gating.
/// </summary>
[Collection("PostgreSQL")]
public sealed class CustomFieldIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly FakeDataScope _scope = new();
    private readonly HashSet<string> _denied = new(StringComparer.Ordinal);
    private bool _showCustomFields;

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => !_denied.Contains(call.ArgAt<string>(1)));
        services.AddSingleton(_permissions);
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService>(new ToggleableAccessSettings(() => _showCustomFields));
        services.AddSingleton<IUserAccountLookup>(new FakeUserAccountLookup());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Definitions_CreateUpdateDeactivateReactivate_KeyAndDataTypeStayImmutable()
    {
        SetUser();

        var created = await ExecuteCommandAsync(new CreateCustomFieldDefinitionCommand(
            "renk", "Renk", CustomFieldDataType.SingleSelect, CustomFieldSection.Other, ["Kırmızı", "Mavi"], IsRequired: false, IsEmployeeEditable: true, EmployeeEditRequiresApproval: false));
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        var def = created.Value;
        def.Key.ShouldBe("renk");
        def.DataType.ShouldBe(CustomFieldDataType.SingleSelect);

        ShouldFail(
            await ExecuteCommandAsync(new CreateCustomFieldDefinitionCommand("renk", "Başka", CustomFieldDataType.Text, CustomFieldSection.Other, null, false, false, false)),
            EmployeeErrors.CustomFieldKeyTaken);

        var updated = await ExecuteCommandAsync(new UpdateCustomFieldDefinitionCommand(
            def.Id, "Renk Tercihi", CustomFieldSection.Personal, ["Yeşil"], IsRequired: true, IsEmployeeEditable: true, EmployeeEditRequiresApproval: false, IsReportable: true, IsSensitive: false, SortOrder: 3));
        updated.IsSuccess.ShouldBeTrue();
        updated.Value.ShouldSatisfyAllConditions(
            d => d.Key.ShouldBe("renk"),
            d => d.DataType.ShouldBe(CustomFieldDataType.SingleSelect),
            d => d.Name.ShouldBe("Renk Tercihi"),
            d => d.Options.ShouldBe(["Yeşil"]),
            d => d.IsRequired.ShouldBeTrue());

        (await ExecuteCommandAsync(new DeactivateCustomFieldDefinitionCommand(def.Id))).Value.IsActive.ShouldBeFalse();
        ShouldFail(await ExecuteCommandAsync(new DeactivateCustomFieldDefinitionCommand(def.Id)), CustomFieldDomainErrors.AlreadyInactive);
        (await ExecuteCommandAsync(new ReactivateCustomFieldDefinitionCommand(def.Id))).Value.IsActive.ShouldBeTrue();

        var list = await ExecuteQueryAsync(new ListCustomFieldDefinitionsQuery());
        list.Value.ShouldHaveSingleItem().Id.ShouldBe(def.Id);
    }

    [Fact]
    public async Task Definitions_ManageAndViewPermissionsAreSeparate()
    {
        SetUser();

        _denied.Add(EmployeePermissions.CustomFieldManageKey);
        ShouldFail(
            await ExecuteCommandAsync(new CreateCustomFieldDefinitionCommand("k", "K", CustomFieldDataType.Text, CustomFieldSection.Other, null, false, false, false)),
            ErrorCodes.Forbidden);

        _denied.Clear();
        _denied.Add(EmployeePermissions.CustomFieldViewKey);
        ShouldFail(await ExecuteQueryAsync(new ListCustomFieldDefinitionsQuery()), ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task SelectAndOtherTypes_RejectUnknownOptionsAndWrongShapedValues()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var color = await CreateDefinitionAsync("renk", CustomFieldDataType.SingleSelect, options: ["Kırmızı", "Mavi"], isEmployeeEditable: true);
        var skills = await CreateDefinitionAsync("beceriler", CustomFieldDataType.MultiSelect, options: ["C#", "SQL"], isEmployeeEditable: true);
        var since = await CreateDefinitionAsync("uye_tarihi", CustomFieldDataType.Date);
        var score = await CreateDefinitionAsync("puan", CustomFieldDataType.Number);
        var flag = await CreateDefinitionAsync("aktif_mi", CustomFieldDataType.Boolean);

        var ok = await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?>
        {
            [color] = "kırmızı",
            [skills] = "[\"c#\",\"SQL\",\"c#\"]",
            [since] = "2024-01-15",
            [score] = "9.5",
            [flag] = "true",
        }));
        ok.IsSuccess.ShouldBeTrue(ok.IsFailure ? ok.Error.Code : null);
        var byDef = ok.Value.ToDictionary(v => v.DefinitionId, v => v.Value);
        byDef[color].ShouldBe("Kırmızı");
        byDef[skills].ShouldBe("[\"C#\",\"SQL\"]");
        byDef[since].ShouldBe("2024-01-15");
        byDef[score].ShouldBe("9.5");
        byDef[flag].ShouldBe("true");

        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [color] = "yeşil" })), CustomFieldDomainErrors.OptionUnknown);
        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [since] = "not-a-date" })), CustomFieldDomainErrors.ValueInvalid);
        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [score] = "abc" })), CustomFieldDomainErrors.ValueInvalid);
        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [flag] = "yes" })), CustomFieldDomainErrors.ValueInvalid);
        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [Guid.NewGuid()] = "x" })), EmployeeErrors.CustomFieldDefinitionNotFound);
    }

    [Fact]
    public async Task RequiredField_RejectsClearing_ButOmittedFieldsAreLeftAlone()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var required = await CreateDefinitionAsync("zorunlu", CustomFieldDataType.Text, isRequired: true);
        var other = await CreateDefinitionAsync("digeri", CustomFieldDataType.Text);

        var first = await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [required] = "değer", [other] = "x" }));
        first.IsSuccess.ShouldBeTrue();

        ShouldFail(await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [required] = null })), CustomFieldDomainErrors.ValueRequired);

        // Touching only "other" leaves the required field's stored value alone.
        var second = await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [other] = "y" }));
        second.Value.First(v => v.DefinitionId == required).Value.ShouldBe("değer");
        second.Value.First(v => v.DefinitionId == other).Value.ShouldBe("y");
    }

    [Fact]
    public async Task SensitiveField_PreservedWithoutSensitiveView_AndHiddenOnRead()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var secret = await CreateDefinitionAsync("gizli", CustomFieldDataType.Text, isSensitive: true);

        (await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [secret] = "orijinal" }))).IsSuccess.ShouldBeTrue();

        _denied.Add(EmployeePermissions.SensitiveViewKey);
        var attempted = await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [secret] = "değiştirilmiş" }));
        attempted.IsSuccess.ShouldBeTrue();
        attempted.Value.First(v => v.DefinitionId == secret).Value.ShouldBeNull();

        var card = await ExecuteQueryAsync(new GetEmployeeQuery(ali));
        card.Value.CustomFields!.First(v => v.DefinitionId == secret).Value.ShouldBeNull();
        _denied.Clear();

        var stored = await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?>()));
        stored.Value.First(v => v.DefinitionId == secret).Value.ShouldBe("orijinal");
    }

    [Fact]
    public async Task SelfService_OnlyOwnRecord_AndOnlyEmployeeEditableDefinitions()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var editable = await CreateDefinitionAsync("hobiler", CustomFieldDataType.Text, isEmployeeEditable: true);
        var hrOnly = await CreateDefinitionAsync("performans_notu", CustomFieldDataType.Text, isEmployeeEditable: false);

        SetUser(employeeId: ali, tenantId: TenantContextAccessor!.TenantId);

        var updated = await ExecuteCommandAsync(new UpdateMyProfileCommand(
            "kendi@example.com", "0555 111 22 33", null, "Adres 1", "İstanbul", "34000",
            new Dictionary<Guid, string?> { [editable] = "Kitap okumak" }));
        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
        updated.Value.PersonalEmail.ShouldBe("kendi@example.com");
        updated.Value.CustomFields!.First(v => v.DefinitionId == editable).Value.ShouldBe("Kitap okumak");

        // Not employee-editable: self-service must not be able to touch it, regardless of value.
        ShouldFail(
            await ExecuteCommandAsync(new UpdateMyProfileCommand(null, null, null, null, null, null, new Dictionary<Guid, string?> { [hrOnly] = "x" })),
            EmployeeErrors.CustomFieldNotEmployeeEditable,
            ErrorType.Forbidden);

        // No linked employee at all -> not found (self-service is always "my own record", never someone else's).
        SetUser(employeeId: null, tenantId: TenantContextAccessor!.TenantId);
        ShouldFail(await ExecuteCommandAsync(new UpdateMyProfileCommand(null, null, null, null, null, null, null)), EmployeeErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task HrPath_CanTouchAnyActiveDefinition_ButIsStillScopedToAuthorizedEmployees()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var outsider = await CreateEmployeeAsync();
        var hrOnly = await CreateDefinitionAsync("performans_notu", CustomFieldDataType.Text, isEmployeeEditable: false);

        (await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [hrOnly] = "İyi" }))).IsSuccess.ShouldBeTrue();

        _scope.Visible = new HashSet<Guid> { ali };
        ShouldFail(
            await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(outsider, new Dictionary<Guid, string?> { [hrOnly] = "x" })),
            ErrorCodes.OutOfScope, ErrorType.NotFound);
    }

    [Fact]
    public async Task CardVisibility_OwnerAndUpdatersAlwaysSee_OthersGatedByAccessSetting()
    {
        SetUser();
        var ali = await CreateEmployeeAsync();
        var field = await CreateDefinitionAsync("not", CustomFieldDataType.Text);
        (await ExecuteCommandAsync(new SetEmployeeCustomFieldsCommand(ali, new Dictionary<Guid, string?> { [field] = "değer" }))).IsSuccess.ShouldBeTrue();

        // employee.employees.update present (default HR caller): always visible regardless of the access setting.
        _showCustomFields = false;
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali))).Value.CustomFields!.Single().Value.ShouldBe("değer");

        // A caller who can read but not update: gated by AccountShowCustomFields.
        _denied.Add(EmployeePermissions.EmployeesUpdateKey);
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali))).Value.CustomFields!.ShouldBeEmpty();
        _showCustomFields = true;
        (await ExecuteQueryAsync(new GetEmployeeQuery(ali))).Value.CustomFields!.Single().Value.ShouldBe("değer");
        _denied.Clear();
    }

    private async Task<Guid> CreateEmployeeAsync()
    {
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand("Ali", "Kaya", Tckn.Next(), null, null, null, new DateOnly(2024, 1, 1), null, null, null));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value.Id;
    }

    private async Task<Guid> CreateDefinitionAsync(
        string key, CustomFieldDataType dataType, IReadOnlyList<string>? options = null, bool isRequired = false, bool isEmployeeEditable = false, bool isSensitive = false)
    {
        var result = await ExecuteCommandAsync(new CreateCustomFieldDefinitionCommand(key, key, dataType, CustomFieldSection.Other, options, isRequired, isEmployeeEditable, false, IsSensitive: isSensitive));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value.Id;
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

    private sealed class ToggleableAccessSettings(Func<bool> enabled) : IAccessSettingsService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(enabled());

        public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(enabled());
    }
}
