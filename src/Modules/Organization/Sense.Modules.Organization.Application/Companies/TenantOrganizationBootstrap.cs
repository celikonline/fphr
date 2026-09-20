using FluentValidation;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// Yeni bir kiracının Organization varsayılanlarını hazırlar (idempotent): sistem birim tipleri, kök Company birimi + Company
/// satırı ve kiracının kendi "Türkiye Resmi Tatilleri" sistem takvimi. Hem <see cref="TenantProvisionedHandler"/> (Identity
/// olayı) hem kurulum sihirbazının "hazırla" çağrısı aynı uygulamayı kullanır.
/// </summary>
public interface ITenantOrganizationBootstrapper
{
    Task EnsureAsync(Guid tenantId, string companyName, CancellationToken ct);
}

/// <summary>Süper admin yeni bir şirket oluşturduğunda (Identity) kiracının Organization varsayılanlarını kurar.</summary>
public sealed class TenantProvisionedHandler(ITenantOrganizationBootstrapper bootstrapper) : IIntegrationEventHandler<TenantProvisioned>
{
    public Task Handle(TenantProvisioned integrationEvent, CancellationToken cancellationToken) =>
        bootstrapper.EnsureAsync(integrationEvent.TenantId, integrationEvent.TenantName, cancellationToken);
}

/// <summary>
/// Kurulum sihirbazının ilk adımı: Worker olayı henüz işlemediyse (ya da hiç çalışmıyorsa) çağıranın kendi kiracısının
/// Organization varsayılanları senkron olarak hazırlanır. Zaten hazırsa hiçbir şey yapmaz.
/// </summary>
[RequiresPermission(OrganizationPermissions.CompanyProfileManageKey)]
public sealed record EnsureTenantOrganizationCommand(string CompanyName) : ICommand;

public sealed class EnsureTenantOrganizationValidator : AbstractValidator<EnsureTenantOrganizationCommand>
{
    public EnsureTenantOrganizationValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
    }
}

public sealed class EnsureTenantOrganizationHandler(ITenantContext tenant, ITenantOrganizationBootstrapper bootstrapper)
    : ICommandHandler<EnsureTenantOrganizationCommand>
{
    public async Task<Result> Handle(EnsureTenantOrganizationCommand command, CancellationToken cancellationToken)
    {
        await bootstrapper.EnsureAsync(tenant.TenantId, command.CompanyName.Trim(), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
