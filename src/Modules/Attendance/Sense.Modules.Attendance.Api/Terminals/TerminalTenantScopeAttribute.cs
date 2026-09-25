using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>
/// Okutma ucunda kiracı bağlamını YALNIZ doğrulanmış terminal kimliğinden kurar (RequestContextMiddleware yalnız JWT'yi okur). Tüm MVC
/// filtrelerinden önce çalışır (en düşük sıra), böylece ModuleAccessFilter terminalin kiracısında modül kapalıysa ucu kapatır ve IPunchRecorder
/// ortam kiracısını girişle karşılaştırabilir. Terminal kimliği yoksa 401.
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

        var setter = context.HttpContext.RequestServices.GetRequiredService<ITenantContextSetter>();
        using var scope = setter.BeginScope(terminal.TenantId);
        await next().ConfigureAwait(false);
    }
}
