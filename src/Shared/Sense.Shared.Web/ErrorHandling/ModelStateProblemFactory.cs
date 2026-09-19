using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Shared.Web.ErrorHandling;

/// <summary>
/// Model-binding (JSON gövde/sorgu dizgisi) hatalarını FluentValidation hatalarıyla aynı ProblemDetails kalıbına
/// (<c>general.validation_error</c>, <c>errors</c> sözlüğü) çevirir. Framework'ün varsayılan iletisi İngilizce'dir ve CLR tip adını
/// sızdırır ("...converted to Sense.Modules.Identity.Api.Controllers.LoginRequest. Path: $.email"); ham hata metni yanıta
/// taşınmaz, yalnızca geçersiz alanın adı ve yerelleştirilmiş genel bir mesaj döner.
/// </summary>
public static class ModelStateProblemFactory
{
    public const string InvalidFieldKey = "general.invalid_field";
    public const string BodyFieldName = "body";

    private const string JsonPathPrefix = "$.";
    private const string JsonPathRoot = "$";

    public static IActionResult Create(ActionContext context)
    {
        var httpContext = context.HttpContext;
        var localizer = httpContext.RequestServices.GetRequiredService<IErrorLocalizer>();
        var problemOptions = httpContext.RequestServices.GetRequiredService<IOptions<ProblemMappingOptions>>().Value;
        var error = new Error(ErrorCodes.ValidationError, ErrorType.Validation);
        var invalidField = localizer.MessageOrText(InvalidFieldKey);

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => NormalizeFieldName(entry.Key))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, _ => new[] { invalidField }, StringComparer.Ordinal);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = localizer.Title(error.Type),
            Detail = localizer.Message(error),
            Type = problemOptions.TypeBaseUrl + error.Code,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions[ProblemDetailsDefaults.CodeExtension] = error.Code;
        problem.Extensions[ProblemDetailsDefaults.TraceIdExtension] = httpContext.TraceIdentifier;
        problem.Extensions[ProblemDetailsDefaults.ErrorsExtension] = errors;

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { MediaTypes.ProblemJson },
        };
    }

    /// <summary>"$.email" → "email"; "$" veya boş anahtar → "body"; boşluk içeren (metin/tip adı taşıyan) anahtarlar da "body" olur.</summary>
    public static string NormalizeFieldName(string key)
    {
        var name = key.StartsWith(JsonPathPrefix, StringComparison.Ordinal) ? key[JsonPathPrefix.Length..] : key;
        return string.IsNullOrWhiteSpace(name) || name == JsonPathRoot || name.Contains(' ', StringComparison.Ordinal)
            ? BodyFieldName
            : name;
    }
}
