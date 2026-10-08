using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

public static class CommercialLicenseRules
{
    public static bool CanAccess(Tenant tenant, DateTime nowUtc) => tenant.IsWritable &&
        (CommercialLicense.Read(tenant.SettingsJson)?.IsAccessible(nowUtc) ?? true);

    public static IReadOnlyList<string> DisabledModules(Tenant tenant)
    {
        var license = CommercialLicense.Read(tenant.SettingsJson);
        return tenant.ModuleSettings.DisabledModules.Concat(license is null ? [] :
            ProductModules.All.Where(m => m.IsToggleable && !license.Modules.Contains(m.Key, StringComparer.Ordinal)).Select(m => m.Key))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

public sealed class CommercialLicenseValidator : AbstractValidator<CommercialLicense>
{
    public CommercialLicenseValidator()
    {
        RuleFor(x => x.Status).Must(s => s is "trial" or "active" or "revoked");
        RuleFor(x => x.Plan).Must(p => p is "starter" or "growth" or "enterprise");
        RuleFor(x => x.EmployeeLimit).InclusiveBetween(1, 100000);
        RuleFor(x => x.ExpiresAt).NotEmpty().Must(d => d.Kind == DateTimeKind.Utc);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Modules).NotNull().Must(m => m is not null && m.Count > 0 && m.Count <= ProductModules.All.Count);
        RuleForEach(x => x.Modules).Must(m => ProductModules.Find(m) is not null);
    }
}

[RequiresPermission(IdentityPermissions.PlatformTenantsManage)]
public sealed record SetCommercialLicenseCommand(Guid TenantId, CommercialLicense License) : ICommand<CommercialLicense>;

public sealed class SetCommercialLicenseValidator : AbstractValidator<SetCommercialLicenseCommand>
{
    public SetCommercialLicenseValidator() => RuleFor(x => x.License).NotNull().SetValidator(new CommercialLicenseValidator());
}

public sealed class SetCommercialLicenseHandler(ITenantRepository tenants, IUserRepository users, ICurrentUser caller,
    IModuleSettingsCacheInvalidator cache) : ICommandHandler<SetCommercialLicenseCommand, CommercialLicense>
{
    public async Task<Result<CommercialLicense>> Handle(SetCommercialLicenseCommand command, CancellationToken cancellationToken)
    {
        var user = caller.UserId is { } id ? await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
        if (user?.IsPlatformStaff != true) return Error.Forbidden(IdentityErrors.PlatformStaffRequired);
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null || PlatformTenant.IsPlatform(tenant.Slug)) return Error.NotFound(IdentityErrors.TenantNotFound);
        if (CommercialLicense.Read(tenant.SettingsJson) is null) return Error.Rule("commercial.license.not_managed");
        tenant.UpdateSettings(command.License.WriteTo(tenant.SettingsJson));
        await cache.InvalidateTenantAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        return command.License;
    }
}

public sealed record GetMyCommercialLicenseQuery : IQuery<CommercialLicense?>;

public sealed class GetMyCommercialLicenseHandler(ITenantRepository tenants, ITenantContext context)
    : IQueryHandler<GetMyCommercialLicenseQuery, CommercialLicense?>
{
    public async Task<Result<CommercialLicense?>> Handle(GetMyCommercialLicenseQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(context.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return Error.NotFound(IdentityErrors.TenantNotFound);
        return Result.Success<CommercialLicense?>(CommercialLicense.Read(tenant.SettingsJson));
    }
}
