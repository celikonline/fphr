using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>
/// G-3-01 (gün açılışı TALIMAT'ı): fesih kaydı düştüğü an, çalışana bağlı kullanıcının sistem erişimi kapanır ("son
/// gün belgelerini indirsin" ihtiyacı İK üzerinden karşılanır - backlog B-3-02; bu köprü Faz 4'ün Onboarding
/// modülünde tekrar yazılmaz). Kullanıcısı yoksa veya zaten pasifse no-op (idempotent - outbox yeniden teslimatı
/// hata üretmez). Koruma: hedef kullanıcı kiracının tek aktif Owner'ıysa devre dışı bırakılmaz (kiracı
/// kilitlenmesin), yalnız uyarı loglanır - <see cref="UserCommands"/>'taki SetUserStatusHandler/SetUserRolesHandler
/// ile aynı "son Owner" kuralı.
/// İlk sürüm: ileri tarihli fesihte de erişim kayıt anında kapanır (PO G-3-09, backlog B-3-03) - <see cref="EmployeeTerminated"/>
/// olayına Faz 2'nin eklediği olası <c>TerminationDate</c> alanı burada kullanılmaz, olay her zaman anında işlenir.
/// </summary>
public sealed class EmployeeTerminatedHandler(
    IUserRepository users,
    IRoleRepository roles,
    IIdentityUnitOfWork unitOfWork,
    ILogger<EmployeeTerminatedHandler> logger) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmployeeIdAsync(integrationEvent.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status == UserStatus.Disabled)
        {
            return;
        }

        var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, cancellationToken).ConfigureAwait(false);
        if (ownerRole is not null && user.HasRole(ownerRole.Id)
            && await users.CountActiveOwnersAsync(cancellationToken).ConfigureAwait(false) <= 1)
        {
            logger.LogWarning(
                "Fesih (çalışan {EmployeeId}): kullanıcı {UserId} kiracının tek aktif Owner'ı, erişim kapatılmadı.",
                integrationEvent.EmployeeId, user.Id);
            return;
        }

        user.Disable();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
