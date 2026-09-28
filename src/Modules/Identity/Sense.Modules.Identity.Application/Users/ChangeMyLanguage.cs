using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>
/// FG-001: oturum açmış kullanıcı kendi dil tercihini değiştirir (<c>PUT identity/me/language</c>). İzin anahtarı yok
/// (MeController kalıbı, yalnız [Authorize]). Kabul edilen değerler, <see cref="LocalizationOptions.SupportedCultures"/>
/// içindeki kültürlerin iki harfli dilleridir (tr-TR → tr); yapılandırmaya yeni kültür eklenince (ör. ar-SA) ek kod gerekmez.
/// Bildirimler (push/e-posta) bu tercihi <c>IUserProfileLookup.PreferredLanguage</c> üzerinden okur.
/// </summary>
public sealed record ChangeMyLanguageCommand(string Language) : ICommand;

public sealed class ChangeMyLanguageHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IOptions<LocalizationOptions> localization) : ICommandHandler<ChangeMyLanguageCommand>
{
    private const char CultureSeparator = '-';
    private const int TwoLetterLength = 2;

    public async Task<Result> Handle(ChangeMyLanguageCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var language = command.Language?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SupportedLanguages().Contains(language))
        {
            return Error.Validation(IdentityErrors.UserLanguageUnsupported);
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        user.ChangePreferredLanguage(language);
        return Result.Success();
    }

    private HashSet<string> SupportedLanguages() => localization.Value.SupportedCultures
        .Select(culture => culture.Split(CultureSeparator)[0].Trim().ToLowerInvariant())
        .Where(lang => lang.Length == TwoLetterLength)
        .ToHashSet(StringComparer.Ordinal);
}
