using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Api;
using Sense.Modules.Announcements.Api.Controllers;
using Sense.Modules.Employee.Api.Controllers;
using Sense.Modules.Identity.Api.Controllers;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Leave.Api.Controllers;
using Sense.Modules.Payroll.Api.Controllers;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Authorization;
using Sense.Shared.Web.Localization;
using Sense.Tests.Integration.Identity;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// senseik-057 "Ayarlar → Modüller" uçtan uca: gerçek Postgres + tam Identity pipeline'ı (yetki, UoW) üzerinden
/// kaydet/oku, ardından global <see cref="ModuleAccessFilter"/>'ın gerçek modül controller tipleriyle davranışı:
/// kapalı modül 403 module.disabled, temel modül ve muaf uç (destek) hiç engellenmez, yeniden açma önbelleği
/// geçersizleyip erişimi hemen geri verir.
/// </summary>
[Collection("PostgreSQL")]
public sealed class ModuleToggleIntegrationTests : IdentityStackTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // ModuleAccessFilter'ın ProblemDetails'i için (host'ta AddSenseWeb kaydeder).
        services.AddLocalization(o => o.ResourcesPath = LocalizationDefaults.ResourcesPath);
        services.TryAddSingleton<IErrorLocalizer, ErrorLocalizer>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModuleSettings_RoundTrip_PersistsToPostgres_AndShowsUpInMe()
    {
        var tenantId = await SeedTenantAsync("acme");
        ActAs(tenantId, await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner));

        var initial = await ExecuteQueryAsync(new GetModuleSettingsQuery());
        initial.IsSuccess.ShouldBeTrue();
        initial.Value.DisabledModules.ShouldBeEmpty();

        (await ExecuteCommandAsync(new UpdateModuleSettingsCommand(["payroll", "leave"]))).IsSuccess.ShouldBeTrue();

        var freshDb = Get<IdentityDbContext>();
        var stored = await freshDb.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, Ct);
        stored.ModuleSettings.DisabledModules.ShouldBe(["leave", "payroll"]);

        var me = await ExecuteQueryAsync(new GetMeQuery());
        me.IsSuccess.ShouldBeTrue();
        me.Value.Features.DisabledModules.ShouldBe(["leave", "payroll"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_RejectsCoreModule_WithLocalizedValidationCode()
    {
        var tenantId = await SeedTenantAsync("acme");
        ActAs(tenantId, await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner));

        var result = await ExecuteCommandAsync(new UpdateModuleSettingsCommand(["employee"]));

        ShouldFailWith(result, ProductModuleErrors.NotToggleable, ErrorType.Validation);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_RequiresModulesManagePermission()
    {
        var tenantId = await SeedTenantAsync("acme");
        ActAs(tenantId, await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR));

        var result = await ExecuteCommandAsync(new UpdateModuleSettingsCommand(["leave"]));

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Filter_BlocksDisabledModule_NeverBlocksCoreOrExempt_AndReenableRestoresAccess()
    {
        var tenantId = await SeedTenantAsync("acme");
        ActAs(tenantId, await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner));

        // Başlangıç: hepsi açık — bu çağrı ayrıca "açık" sonucunu önbelleğe yazar (geçersizleme aşağıda doğrulanır).
        (await RunFilterAsync(typeof(MyLeaveController))).ShouldBeNull();

        (await ExecuteCommandAsync(new UpdateModuleSettingsCommand(["leave", "announcements"]))).IsSuccess.ShouldBeTrue();

        var blocked = await RunFilterAsync(typeof(MyLeaveController));
        var problemResult = blocked.ShouldBeOfType<ObjectResult>();
        problemResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        var problem = problemResult.Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions[ProblemDetailsDefaults.CodeExtension].ShouldBe(ProductModuleErrors.Disabled);
        problem.Detail.ShouldNotBeNullOrWhiteSpace();

        // Temel modül (Employee), kapatılmamış bir modül (Payroll), Modüller ayar ucu (Identity) ve muaf destek ucu geçer.
        (await RunFilterAsync(typeof(EmployeesController))).ShouldBeNull();
        (await RunFilterAsync(typeof(PayrollPeriodsController))).ShouldBeNull();
        (await RunFilterAsync(typeof(ModuleSettingsController))).ShouldBeNull();
        (await RunFilterAsync(typeof(SupportController))).ShouldBeNull();

        // Yeniden açma: PUT önbelleği geçersizler, erişim hemen geri gelir.
        (await ExecuteCommandAsync(new UpdateModuleSettingsCommand([]))).IsSuccess.ShouldBeTrue();
        (await RunFilterAsync(typeof(MyLeaveController))).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Filter_IsTenantScoped_DisablingInTenantA_DoesNotBlockTenantB()
    {
        var tenantA = await SeedTenantAsync("tenant-a");
        var tenantB = await SeedTenantAsync("tenant-b");
        var ownerA = await SeedUserAsync(tenantA, "owner@a.test", SystemRoleCodes.Owner);
        var ownerB = await SeedUserAsync(tenantB, "owner@b.test", SystemRoleCodes.Owner);

        ActAs(tenantA, ownerA);
        (await ExecuteCommandAsync(new UpdateModuleSettingsCommand(["leave"]))).IsSuccess.ShouldBeTrue();
        (await RunFilterAsync(typeof(MyLeaveController))).ShouldNotBeNull();

        ActAs(tenantB, ownerB);
        (await RunFilterAsync(typeof(MyLeaveController))).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LegacyRow_WithEmptyJsonObject_ReadsAsAllEnabled()
    {
        var tenantId = await SeedTenantAsync("legacy");
        var db = Get<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE identity.tenants SET module_settings = '{}'::jsonb WHERE id = {0}", [tenantId], Ct);
        ActAs(tenantId, await SeedUserAsync(tenantId, "owner@legacy.test", SystemRoleCodes.Owner));

        var fresh = await Get<IdentityDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, Ct);
        fresh.ModuleSettings.DisabledModules.ShouldBeEmpty();
        (await RunFilterAsync(typeof(MyLeaveController))).ShouldBeNull();
    }

    [Fact]
    public void EveryHostedModule_IsInTheProductModuleCatalog()
    {
        // Yeni bir modül ModuleCatalog'a eklenip ProductModules'a eklenmezse uçları hiçbir zaman engellenmez
        // ve Modüller sayfasında görünmez — bu test o unutkanlığı yakalar.
        var missing = ModuleCatalog.Modules
            .Select(m => m.GetType().Assembly.GetName().Name)
            .Where(name => ProductModules.FromAssemblyName(name) is null)
            .ToList();

        missing.ShouldBeEmpty();
    }

    private async Task<IActionResult?> RunFilterAsync(Type controllerType)
    {
        var method = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).First();
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controllerType.GetTypeInfo(),
            MethodInfo = method,
            ControllerName = controllerType.Name,
            ActionName = method.Name,
        };
        var httpContext = new DefaultHttpContext { RequestServices = ServiceProvider! };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var context = new ResourceExecutingContext(actionContext, [], []);

        var nextCalled = false;
        await new ModuleAccessFilter().OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(actionContext, []));
        });

        return nextCalled ? null : context.Result.ShouldNotBeNull();
    }
}
