using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Desteklenen tenant dili/bölgesi (web i18n: tr, en, de).</summary>
public static class SupportedTenantLocales
{
    public static readonly IReadOnlyList<string> All = ["tr-TR", "en-US", "de-DE"];
}

/// <summary>Kurulum sihirbazının "Şirket bilgileri" adımı: kiracının adı, vergi numarası, dili ve saat dilimi.</summary>
[RequiresPermission(IdentityPermissions.TenantView)]
public sealed record GetTenantProfileQuery : IQuery<TenantDto>;

public sealed class GetTenantProfileHandler(ITenantRepository tenants, ITenantContext tenant) : IQueryHandler<GetTenantProfileQuery, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(GetTenantProfileQuery query, CancellationToken cancellationToken)
    {
        var entity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        return entity is null ? Error.NotFound(IdentityErrors.TenantNotFound) : ToDto(entity);
    }

    internal static TenantDto ToDto(Tenant t) =>
        new(t.Id, t.Slug, t.Name, t.TaxNumber, t.Status.ToString(), t.TrialEndsAt, t.Locale, t.TimeZone, t.IsSetupCompleted);
}

[RequiresPermission(IdentityPermissions.TenantManage)]
public sealed record UpdateTenantProfileCommand(string Name, string? TaxNumber, string Locale, string TimeZone) : ICommand<TenantDto>;

public sealed class UpdateTenantProfileValidator : AbstractValidator<UpdateTenantProfileCommand>
{
    private const int TaxNumberMaxLength = 20;

    public UpdateTenantProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(IdentityLimits.CompanyNameMaxLength);
        RuleFor(x => x.TaxNumber).MaximumLength(TaxNumberMaxLength);
        RuleFor(x => x.Locale).Must(locale => SupportedTenantLocales.All.Contains(locale, StringComparer.Ordinal))
            .WithMessage(IdentityErrors.TenantInvalidLocale);
        RuleFor(x => x.TimeZone).Must(BeKnownTimeZone).WithMessage(IdentityErrors.TenantInvalidTimeZone);
    }

    private static bool BeKnownTimeZone(string id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}

public sealed class UpdateTenantProfileHandler(ITenantRepository tenants, ITenantContext tenant) : ICommandHandler<UpdateTenantProfileCommand, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(UpdateTenantProfileCommand command, CancellationToken cancellationToken)
    {
        var entity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        entity.Rename(command.Name.Trim());
        entity.SetTaxNumber(command.TaxNumber);
        entity.SetLocale(command.Locale, command.TimeZone);
        return GetTenantProfileHandler.ToDto(entity);
    }
}

/// <summary>Kurulum sihirbazının son adımı: kurulum tamamlandı işaretlenir (idempotent).</summary>
[RequiresPermission(IdentityPermissions.TenantManage)]
public sealed record CompleteTenantSetupCommand : ICommand;

public sealed class CompleteTenantSetupHandler(ITenantRepository tenants, ITenantContext tenant, TimeProvider clock) : ICommandHandler<CompleteTenantSetupCommand>
{
    public async Task<Result> Handle(CompleteTenantSetupCommand command, CancellationToken cancellationToken)
    {
        var entity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        entity.CompleteSetup(clock.GetUtcNow().UtcDateTime);
        return Result.Success();
    }
}
