using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.UnitAuthorizations;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.UnitAuthorizations;

/// <summary>
/// Birim ağacı satır üç nokta menüsü "Birim yetkilendirme" &gt; kişi seç &gt; iki anahtar &gt; Kaydet
/// (roadmap 11.3.1/11.3.2). Aynı (OrgUnit, User) çifti için zaten aktif bir grant varsa yeni satır
/// oluşturmak yerine mevcut kaydın anahtarları güncellenir (upsert) - ekranın "Kaydet" davranışı budur;
/// farklı bir birim için aynı kişiye ayrı bir grant oluşturur ("aynı kişi birden çok birime").
/// </summary>
[RequiresPermission(IdentityPermissions.UnitAuthorizationsManage)]
public sealed record GrantUnitAuthorizationCommand(Guid OrgUnitId, Guid UserId, bool CanViewSalaryAndPayments, bool CanViewUnitReports) : ICommand<Guid>;

public sealed class GrantUnitAuthorizationValidator : AbstractValidator<GrantUnitAuthorizationCommand>
{
    public GrantUnitAuthorizationValidator()
    {
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.CanViewSalaryAndPayments || x.CanViewUnitReports)
            .WithMessage(IdentityErrors.UnitAuthorizationNoGrantsSelected);
    }
}

public sealed class GrantUnitAuthorizationHandler(
    IUnitAuthorizationRepository grants,
    IUserRepository users,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IPermissionCacheInvalidator cache,
    TimeProvider clock,
    IOrgUnitLookup? orgUnitLookup = null) : ICommandHandler<GrantUnitAuthorizationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(GrantUnitAuthorizationCommand command, CancellationToken cancellationToken)
    {
        if (orgUnitLookup is not null &&
            !await orgUnitLookup.ExistsActiveAsync(tenant.TenantId, command.OrgUnitId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound(IdentityErrors.OrgUnitNotFound);
        }

        var targetUser = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (targetUser is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (currentUser.UserId is not { } grantedByUserId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Upsert: "Kaydet" ile aynı (OrgUnit, User) için tekrar gönderilen anahtarlar mevcut aktif
        // grant'i günceller (yeni satır + unique index çakışması yerine).
        var existing = await grants.GetActiveAsync(command.OrgUnitId, command.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var updated = existing.UpdateGrants(command.CanViewSalaryAndPayments, command.CanViewUnitReports, grantedByUserId, now);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            // Bu görev DataScope çözümleme pipeline'ını henüz uygulamıyor (bkz. UnitAuthorization doc);
            // yine de "pv" invalidation'ı roadmap tasarımına göre şimdiden yapılır, ileride bir
            // IDataScopeService/IEmployeeScopeLookup tüketicisi eklendiğinde cache tutarlılığı hazır olsun diye.
            await cache.InvalidateUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var created = UnitAuthorization.Grant(
            tenant.TenantId, command.OrgUnitId, command.UserId, command.CanViewSalaryAndPayments, command.CanViewUnitReports, grantedByUserId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        grants.Add(created.Value);
        await cache.InvalidateUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

/// <summary>Birim yetkilendirmesini kaldırır (soft-revoke: IsActive=false, geçmiş korunur).</summary>
[RequiresPermission(IdentityPermissions.UnitAuthorizationsManage)]
public sealed record RevokeUnitAuthorizationCommand(Guid UnitAuthorizationId) : ICommand;

public sealed class RevokeUnitAuthorizationHandler(IUnitAuthorizationRepository grants, IPermissionCacheInvalidator cache, TimeProvider clock)
    : ICommandHandler<RevokeUnitAuthorizationCommand>
{
    public async Task<Result> Handle(RevokeUnitAuthorizationCommand command, CancellationToken cancellationToken)
    {
        var grant = await grants.GetByIdAsync(command.UnitAuthorizationId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound(IdentityErrors.UnitAuthorizationNotFound);
        }

        var revoked = grant.Revoke(clock.GetUtcNow().UtcDateTime);
        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        await cache.InvalidateUserAsync(grant.UserId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Bir birimin üç nokta menüsündeki "Birim yetkilendirme" ekranının mevcut (aktif) grant listesi.</summary>
[RequiresPermission(IdentityPermissions.UnitAuthorizationsRead)]
public sealed record ListUnitAuthorizationsQuery(Guid OrgUnitId) : IQuery<IReadOnlyList<UnitAuthorizationDto>>;

public sealed class ListUnitAuthorizationsHandler(IUnitAuthorizationRepository grants)
    : IQueryHandler<ListUnitAuthorizationsQuery, IReadOnlyList<UnitAuthorizationDto>>
{
    public async Task<Result<IReadOnlyList<UnitAuthorizationDto>>> Handle(ListUnitAuthorizationsQuery query, CancellationToken cancellationToken)
    {
        var list = await grants.ListActiveByOrgUnitAsync(query.OrgUnitId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UnitAuthorizationDto> dtos = list.Select(UnitAuthorizationDto.From).ToList();
        return Result.Success(dtos);
    }
}
