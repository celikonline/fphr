using System.Reflection;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Shared.Infrastructure.Messaging.Behaviours;

/// <summary>
/// [RequiresAccessSetting] ile işaretli isteklerde kiracının "Erişim ayarları" anahtarı denetlenir.
/// <see cref="AuthorizationBehaviour{TRequest,TResponse}"/> ile aynı desen ("izin var mı" yerine "bu özellik açık mı").
/// Etkin değer çağırana göre çözülür: bypass izni (Owner/Admin/HR) veya aktif impersonation oturumu ayarı aşar.
/// Attribute yoksa davranış no-op'tur.
/// </summary>
public sealed class AccessSettingsAuthorizationBehaviour<TRequest, TResponse>(IAccessSettingsService accessSettings) : IPipelineBehaviour<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private const string AccessSettingArg = "accessSetting";

    private static readonly RequiresAccessSettingAttribute[] Required =
        typeof(TRequest).GetCustomAttributes<RequiresAccessSettingAttribute>(inherit: true).ToArray();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Required.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        foreach (var attr in Required)
        {
            if (!await accessSettings.IsEnabledForCurrentUserAsync(attr.Key, cancellationToken).ConfigureAwait(false))
            {
                return Fail(Error.Forbidden(ErrorCodes.FeatureDisabled, (AccessSettingArg, attr.Key)));
            }
        }

        return await next().ConfigureAwait(false);
    }

    private static TResponse Fail(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(error);
        }

        var valueType = typeof(TResponse).GetGenericArguments()[0];
        var method = typeof(Result).GetMethod(nameof(Result.Failure), 1, [typeof(Error)])!.MakeGenericMethod(valueType);
        return (TResponse)method.Invoke(null, [error])!;
    }
}
