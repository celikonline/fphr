using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Süper admin listesi satırı. <paramref name="IsPlatform"/>: platform kiracısının kendisi (müşteri değil).</summary>
public sealed record TenantSummaryDto(Guid Id, string Slug, string Name, TenantStatus Status, DateTime? TrialEndsAt, DateTime CreatedAt, bool IsPlatform, string? TaxNumber, string Locale, string TimeZone);

/// <summary>Tüm şirketleri (kiracıları) listeler. Yalnız süper admin: izin + <c>User.IsPlatformStaff</c> (bkz. <see cref="CreateTenantCommand"/>).</summary>
[RequiresPermission(IdentityPermissions.PlatformTenantsManage)]
public sealed record ListTenantsQuery : IQuery<IReadOnlyList<TenantSummaryDto>>;

public sealed class ListTenantsHandler(ITenantRepository tenants, IUserRepository users, ICurrentUser currentUser)
    : IQueryHandler<ListTenantsQuery, IReadOnlyList<TenantSummaryDto>>
{
    public async Task<Result<IReadOnlyList<TenantSummaryDto>>> Handle(ListTenantsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var caller = await users.GetByIdAsync(callerId, cancellationToken).ConfigureAwait(false);
        if (caller is null || !caller.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.PlatformStaffRequired);
        }

        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        return all.Select(ToSummary).ToList();
    }

    internal static TenantSummaryDto ToSummary(Tenant t) =>
        new(t.Id, t.Slug, t.Name, t.Status, t.TrialEndsAt, t.CreatedAt, PlatformTenant.IsPlatform(t.Slug), t.TaxNumber, t.Locale, t.TimeZone);
}

/// <summary>
/// Süper admin: bir şirketin (kiracının) bilgilerini düzenler — ad, vergi numarası, dil ve saat dilimi. Slug değişmez
/// (girişte ve bağlantılarda kullanılır). Yetki kalıbı <see cref="ListTenantsQuery"/> ile aynı.
/// </summary>
[RequiresPermission(IdentityPermissions.PlatformTenantsManage)]
public sealed record UpdateTenantByPlatformCommand(Guid TenantId, string Name, string? TaxNumber, string Locale, string TimeZone)
    : ICommand<TenantSummaryDto>;

public sealed class UpdateTenantByPlatformValidator : AbstractValidator<UpdateTenantByPlatformCommand>
{
    private const int TaxNumberMaxLength = 20;

    public UpdateTenantByPlatformValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(IdentityLimits.CompanyNameMaxLength);
        RuleFor(x => x.TaxNumber).MaximumLength(TaxNumberMaxLength);
        RuleFor(x => x.Locale).Must(locale => SupportedTenantLocales.All.Contains(locale, StringComparer.Ordinal))
            .WithMessage(IdentityErrors.TenantInvalidLocale);
        RuleFor(x => x.TimeZone).Must(id => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage(IdentityErrors.TenantInvalidTimeZone);
    }
}

public sealed class UpdateTenantByPlatformHandler(ITenantRepository tenants, IUserRepository users, ICurrentUser currentUser)
    : ICommandHandler<UpdateTenantByPlatformCommand, TenantSummaryDto>
{
    public async Task<Result<TenantSummaryDto>> Handle(UpdateTenantByPlatformCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var caller = await users.GetByIdAsync(callerId, cancellationToken).ConfigureAwait(false);
        if (caller is null || !caller.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.PlatformStaffRequired);
        }

        var entity = await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        entity.Rename(command.Name.Trim());
        entity.SetTaxNumber(command.TaxNumber);
        entity.SetLocale(command.Locale, command.TimeZone);
        return ListTenantsHandler.ToSummary(entity);
    }
}
