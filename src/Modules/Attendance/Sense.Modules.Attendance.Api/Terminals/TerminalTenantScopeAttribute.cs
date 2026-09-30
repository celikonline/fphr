using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>
/// Okutma ucunda istek bağlamını YALNIZ doğrulanmış terminal kimliğinden kurar (RequestContextMiddleware yalnız JWT'yi okur): kiracı kapsamı,
/// denetim aktörü (<see cref="TerminalActor"/>, "terminal:{kod}") ve denetim kaynağı (Api). Tüm MVC filtrelerinden önce çalışır (en düşük
/// sıra), böylece ModuleAccessFilter terminalin kiracısında modül kapalıysa ucu kapatır ve IPunchRecorder ortam kiracısını girişle
/// karşılaştırabilir. Terminal kimliği yoksa 401.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TerminalTenantScopeAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (TerminalPrincipal.Read(context.HttpContext.User) is not { } terminal)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var services = context.HttpContext.RequestServices;
        using var tenantScope = services.GetRequiredService<ITenantContextSetter>().BeginScope(terminal.TenantId);
        using var actorScope = CurrentUserAccessor.Use(new TerminalActor(terminal));
        using var sourceScope = services.GetService<IAuditSourceAccessor>()?.BeginScope(AuditSources.Api);
        await next().ConfigureAwait(false);
    }
}

/// <summary>
/// Terminal isteğinin <see cref="ICurrentUser"/>'ı: yalnız denetim aktörü adını taşır ("terminal:{kod}"). Kullanıcı DEĞİLDİR - kimliği
/// doğrulanmış kullanıcı sayılmaz, UserId/rol/platform yetkisi yoktur; böylece izin denetimli hiçbir komut bu bağlamda geçemez.
/// </summary>
public sealed class TerminalActor(TerminalIdentity terminal) : ICurrentUser
{
    private static readonly IReadOnlySet<string> NoRoles = new HashSet<string>(StringComparer.Ordinal);

    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? EmployeeId => null;

    public string? Email => null;

    public string? DisplayName { get; } = TerminalKeyDefaults.AuditActorPrefix + terminal.Code;

    public IReadOnlySet<string> Roles => NoRoles;

    public bool IsPlatformAdmin => false;

    public Guid? ImpersonatorUserId => null;

    public Guid? ApiKeyId => null;

    public string? CorrelationId => null;

    public string? IpAddress => null;
}
