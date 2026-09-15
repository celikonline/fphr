using System.Diagnostics;
using System.Reflection;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Shared.Infrastructure.Messaging.Behaviours;

/// <summary>
/// [RequiresPermission] ile işaretli isteklerde izin, <see cref="IScopedRequest"/> isteklerde hedef çalışan kapsamı denetlenir.
/// API dışından (job, event) çağrılan use case'ler için de aynı kural geçerlidir.
/// </summary>
public sealed class AuthorizationBehaviour<TRequest, TResponse>(
    ICurrentUser user,
    IPermissionService permissions,
    IDataScopeService dataScope) : IPipelineBehaviour<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private const string PermissionArg = "permission";

    private static readonly RequiresPermissionAttribute[] Required =
        typeof(TRequest).GetCustomAttributes<RequiresPermissionAttribute>(inherit: true).ToArray();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Required.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        if (!user.IsAuthenticated || user.UserId is null)
        {
            return Fail(Error.Unauthorized(ErrorCodes.Unauthenticated));
        }

        if (user.IsPlatformAdmin)
        {
            return await next().ConfigureAwait(false);
        }

        foreach (var attr in Required)
        {
            var granted = await permissions.HasAsync(user.UserId.Value, attr.Permission, cancellationToken).ConfigureAwait(false);
            TracePermissionCheck(attr.Permission, granted);

            if (!granted)
            {
                return Fail(Error.Forbidden(ErrorCodes.Forbidden, (PermissionArg, attr.Permission)));
            }

            if (request is IScopedRequest { TargetEmployeeId: { } target }
                && !await dataScope.CanAccessEmployeeAsync(attr.Permission, target, cancellationToken).ConfigureAwait(false))
            {
                // Kapsam dışı hedef 404 döner: varlık sızıntısı yok.
                return Fail(Error.NotFound(ErrorCodes.OutOfScope));
            }
        }

        return await next().ConfigureAwait(false);
    }

    /// <summary>
    /// İzin kararını, isteğin TracingBehaviour tarafından açılmış mevcut span'ine olay olarak ekler
    /// (yeni span açmaz; permission service çağrılarının maliyeti zaten ayrı span'lerinde görünür).
    /// </summary>
    private static void TracePermissionCheck(string permission, bool granted)
    {
        Activity.Current?.AddEvent(new ActivityEvent(
            "sense.permission_check",
            tags: new ActivityTagsCollection
            {
                { "sense.permission", permission },
                { "sense.permission.granted", granted },
            }));
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
