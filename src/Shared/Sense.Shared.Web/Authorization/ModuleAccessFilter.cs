using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Shared.Web.Authorization;

/// <summary>
/// Global MVC filtresi (senseik-057 "Ayarlar → Modüller"): controller'ın assembly'sinden (<c>Sense.Modules.{X}.Api</c>)
/// ürün modülünü çözer; modül kapatılabilir ve mevcut kiracı için kapalıysa 403 + <see cref="ProductModuleErrors.Disabled"/>
/// ProblemDetails döner. Resource filtresi olduğundan model binding'den (ve dosya yüklemelerinden) önce çalışır;
/// [Authorize] yetkilendirme filtreleri ondan da önce çalışır (anonim çağrı 401 alır, modül bilgisi sızmaz).
/// Asla engellenmeyenler: temel modüller (Identity dahil — Modüller ayar uçları orada), katalog dışı assembly'ler,
/// <see cref="ModuleAccessExemptAttribute"/> ile işaretli uçlar ve kiracı bağlamı olmayan istekler.
/// </summary>
public sealed class ModuleAccessFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var module = ResolveGuardedModule(context.ActionDescriptor);
        if (module is not null
            && context.HttpContext.RequestServices.GetService<IModuleAccessService>() is { } access
            && !await access.IsEnabledAsync(module.Key, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            context.Result = CreateProblem(context.HttpContext, module.Key);
            return;
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>Uç, kapatılabilir bir modüle aitse o modül; aksi halde (temel/katalog dışı/muaf) null.</summary>
    public static ProductModule? ResolveGuardedModule(ActionDescriptor descriptor)
    {
        if (descriptor is not ControllerActionDescriptor action)
        {
            return null;
        }

        var module = ProductModules.FromAssembly(action.ControllerTypeInfo.Assembly);
        if (module is null || !module.IsToggleable)
        {
            return null;
        }

        var exempt = action.ControllerTypeInfo.IsDefined(typeof(ModuleAccessExemptAttribute), inherit: true)
            || action.MethodInfo.IsDefined(typeof(ModuleAccessExemptAttribute), inherit: true);
        return exempt ? null : module;
    }

    private static ObjectResult CreateProblem(HttpContext httpContext, string moduleKey)
    {
        var localizer = httpContext.RequestServices.GetRequiredService<IErrorLocalizer>();
        var problemOptions = httpContext.RequestServices.GetRequiredService<IOptions<ProblemMappingOptions>>().Value;
        var error = Error.Forbidden(ProductModuleErrors.Disabled, (ProductModuleErrors.ModuleArg, moduleKey));

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = localizer.Title(error.Type),
            Detail = localizer.Message(error),
            Type = problemOptions.TypeBaseUrl + error.Code,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions[ProblemDetailsDefaults.CodeExtension] = error.Code;
        problem.Extensions[ProblemDetailsDefaults.TraceIdExtension] = httpContext.TraceIdentifier;
        problem.Extensions[ProblemDetailsDefaults.ArgsExtension] = error.Args;

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { MediaTypes.ProblemJson },
        };
    }
}
