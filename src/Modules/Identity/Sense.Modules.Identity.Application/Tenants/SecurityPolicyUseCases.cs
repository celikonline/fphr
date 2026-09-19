using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>
/// Güvenlik politikası sayfasının (Ayarlar &gt; Sistem ayarları &gt; Güvenlik politikası) tek anahtarı: zorunlu 2FA
/// uygulanan rol kodları. <see cref="Domain.Tenants.TenantSecurityPolicy"/> ile bire bir eşleşir.
/// </summary>
public sealed record SecurityPolicyDto(IReadOnlyList<string> RequireMfaForRoles)
{
    public static readonly SecurityPolicyDto Empty = new([]);

    public static SecurityPolicyDto From(TenantSecurityPolicy p) => new(p.RequireMfaForRoles);

    public TenantSecurityPolicy ToDomain() => new() { RequireMfaForRoles = RequireMfaForRoles };
}

/// <summary>Güvenlik politikasını okur (mevcut kiracı bağlamı). Erişim ayarlarıyla aynı desen: "identity.tenant.view"
/// yeterli (profilin bir parçası), düzenleme ayrı ve daha dar bir izin ister (<see cref="UpdateSecurityPolicyCommand"/>).</summary>
[RequiresPermission(IdentityPermissions.TenantView)]
public sealed record GetSecurityPolicyQuery : IQuery<SecurityPolicyDto>;

public sealed class GetSecurityPolicyHandler(ITenantRepository tenants, ITenantContext tenantContext) : IQueryHandler<GetSecurityPolicyQuery, SecurityPolicyDto>
{
    public async Task<Result<SecurityPolicyDto>> Handle(GetSecurityPolicyQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        return SecurityPolicyDto.From(tenant.SecurityPolicy);
    }
}

/// <summary>Güvenlik politikası sayfasının "Kaydet" eylemi: zorunlu 2FA rol listesi birlikte, anında etkili biçimde güncellenir.</summary>
[RequiresPermission(IdentityPermissions.SecurityPolicyManage)]
public sealed record UpdateSecurityPolicyCommand(IReadOnlyList<string> RequireMfaForRoles) : ICommand
{
    public SecurityPolicyDto ToDto() => new(RequireMfaForRoles);
}

public sealed class UpdateSecurityPolicyHandler(ITenantRepository tenants, ITenantContext tenantContext) : ICommandHandler<UpdateSecurityPolicyCommand>
{
    public async Task<Result> Handle(UpdateSecurityPolicyCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        tenant.UpdateSecurityPolicy(command.ToDto().ToDomain());
        return Result.Success();
    }
}
