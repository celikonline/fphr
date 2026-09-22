using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Yeni şirketin ilk kurulumu: kurulum durumu (/me), şirket bilgileri (ad, vergi no, dil, saat dilimi) ve süper admin oluşturma
/// akışının diğer modüllere yayınladığı olay. Kurulumu yalnız tenant yönetimi izni olan roller değiştirebilir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TenantSetupTests : IdentityStackTestBase
{
    private const string StrongPassword = "Gunes-Deniz-2026";

    [Fact]
    public async Task NewTenant_StartsWithSetupPending_AndOwnerCompletesItIdempotently()
    {
        var created = await CreateTenantAsync("acme");
        ActAs(created.TenantId, created.OwnerUserId);

        (await GetSetupCompletedAsync()).ShouldBeFalse();

        (await ExecuteCommandAsync(new CompleteTenantSetupCommand())).IsSuccess.ShouldBeTrue();
        (await GetSetupCompletedAsync()).ShouldBeTrue();

        var firstCompletion = await ReadSetupCompletedAtAsync(created.TenantId);
        firstCompletion.ShouldNotBeNull();
        Time.Advance(TimeSpan.FromHours(1));
        (await ExecuteCommandAsync(new CompleteTenantSetupCommand())).IsSuccess.ShouldBeTrue();
        (await ReadSetupCompletedAtAsync(created.TenantId)).ShouldBe(firstCompletion);
    }

    [Fact]
    public async Task Owner_UpdatesCompanyProfile_AndInvalidValuesAreRejected()
    {
        var created = await CreateTenantAsync("acme");
        ActAs(created.TenantId, created.OwnerUserId);

        var updated = await ExecuteCommandAsync(new UpdateTenantProfileCommand("Acme Teknoloji A.Ş.", "1234567890", "en-US", "Europe/Berlin"));

        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
        updated.Value.Name.ShouldBe("Acme Teknoloji A.Ş.");
        updated.Value.TaxNumber.ShouldBe("1234567890");
        updated.Value.Locale.ShouldBe("en-US");
        updated.Value.TimeZone.ShouldBe("Europe/Berlin");

        var profile = (await ExecuteQueryAsync(new GetTenantProfileQuery())).Value;
        profile.Name.ShouldBe("Acme Teknoloji A.Ş.");
        profile.TimeZone.ShouldBe("Europe/Berlin");

        // Doğrulama hattı (ValidationBehaviour) ValidationException fırlatır; HTTP katmanı bunu 400'e çevirir.
        var badLocale = await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new UpdateTenantProfileCommand("Acme", null, "xx-XX", "Europe/Berlin")));
        badLocale.Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.TenantInvalidLocale);

        var badZone = await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new UpdateTenantProfileCommand("Acme", null, "tr-TR", "Not/AZone")));
        badZone.Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.TenantInvalidTimeZone);

        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new UpdateTenantProfileCommand(" ", null, "tr-TR", "Europe/Istanbul")));

        (await ExecuteQueryAsync(new GetTenantProfileQuery())).Value.Locale.ShouldBe("en-US");
    }

    [Fact]
    public async Task EmployeeRole_CannotChangeProfileOrCompleteSetup()
    {
        var created = await CreateTenantAsync("acme");
        var employee = await SeedUserAsync(created.TenantId, "worker@acme.test", SystemRoleCodes.Employee);

        ActAs(created.TenantId, employee);

        ShouldFailWith(await ExecuteCommandAsync(new UpdateTenantProfileCommand("Hack", null, "tr-TR", "Europe/Istanbul")), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new CompleteTenantSetupCommand()), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    [Fact]
    public async Task CreatingATenant_PublishesTenantProvisionedToTheIdentityOutbox()
    {
        var created = await CreateTenantAsync("acme");

        var messages = await Get<IdentityDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.TenantId == created.TenantId)
            .ToListAsync(Ct);

        var provisioned = messages.Where(m => m.Type.Contains("TenantProvisioned", StringComparison.Ordinal)).ToList();
        provisioned.Count.ShouldBe(1);
        provisioned[0].Payload.ShouldContain("Acme A.Ş.");
    }

    private async Task<CreateTenantResult> CreateTenantAsync(string slug)
    {
        var platform = await SeedTenantAsync(PlatformTenant.Slug);
        var superAdmin = await SeedUserAsync(platform, "root@sense.test", SystemRoleCodes.SuperAdmin, platformStaff: true);
        ActAs(platform, superAdmin);

        var result = await ExecuteCommandAsync(
            new CreateTenantCommand("Acme A.Ş.", slug, "boss@acme.test", "Ali", "Veli", TenantOwnerMode.Password, StrongPassword, null, "tr"));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<bool> GetSetupCompletedAsync()
    {
        var me = await ExecuteQueryAsync(new GetMeQuery());
        me.IsSuccess.ShouldBeTrue(me.IsFailure ? me.Error.Code : null);
        return me.Value.Tenant.SetupCompleted;
    }

    private async Task<DateTime?> ReadSetupCompletedAtAsync(Guid tenantId) =>
        (await Get<IdentityDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, Ct)).SetupCompletedAt;
}
