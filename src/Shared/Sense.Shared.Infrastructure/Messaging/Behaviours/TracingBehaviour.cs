using System.Diagnostics;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Shared.Infrastructure.Messaging.Behaviours;

/// <summary>Her istek için OpenTelemetry Activity açar.</summary>
public sealed class TracingBehaviour<TRequest, TResponse> : IPipelineBehaviour<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public static readonly ActivitySource Source = new(DiagnosticsDefaults.ActivitySourceName);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity(typeof(TRequest).Name, ActivityKind.Internal);
        activity?.SetTag(TraceTags.Request, typeof(TRequest).FullName);

        var response = await next().ConfigureAwait(false);

        if (response.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, response.Error.Code);
            activity?.SetTag(TraceTags.ErrorCode, response.Error.Code);
        }

        return response;
    }

    private static class TraceTags
    {
        public const string Request = "sense.request";
        public const string ErrorCode = "sense.error.code";
    }
}
