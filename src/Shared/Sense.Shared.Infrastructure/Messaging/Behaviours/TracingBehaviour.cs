using System.Diagnostics;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure.Observability;
using Sense.Shared.Kernel.Results;

namespace Sense.Shared.Infrastructure.Messaging.Behaviours;

/// <summary>Her istek için OpenTelemetry Activity açar ve süresini <see cref="SenseMeters.DispatcherRequestDuration"/> metriğine yazar.</summary>
public sealed class TracingBehaviour<TRequest, TResponse> : IPipelineBehaviour<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public static readonly ActivitySource Source = new(DiagnosticsDefaults.ActivitySourceName);

    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly string Module = SenseMeters.ModuleOf(typeof(TRequest));
    private static readonly string Kind = typeof(TRequest).GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
        ? SenseMeters.RequestKinds.Query
        : SenseMeters.RequestKinds.Command;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity(RequestName, ActivityKind.Internal);
        activity?.SetTag(TraceTags.Request, typeof(TRequest).FullName);
        activity?.SetTag(SenseMeters.Tags.Module, Module);

        var startedAt = Stopwatch.GetTimestamp();
        var outcome = SenseMeters.Outcomes.Exception;
        try
        {
            var response = await next().ConfigureAwait(false);

            if (response.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, response.Error.Code);
                activity?.SetTag(TraceTags.ErrorCode, response.Error.Code);
            }

            outcome = response.IsFailure ? SenseMeters.Outcomes.Failure : SenseMeters.Outcomes.Success;
            return response;
        }
        finally
        {
            SenseMeters.DispatcherRequestDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
                new KeyValuePair<string, object?>(SenseMeters.Tags.Request, RequestName),
                new KeyValuePair<string, object?>(SenseMeters.Tags.RequestKind, Kind),
                new KeyValuePair<string, object?>(SenseMeters.Tags.Module, Module),
                new KeyValuePair<string, object?>(SenseMeters.Tags.Outcome, outcome));
        }
    }

    private static class TraceTags
    {
        public const string Request = "sense.request";
        public const string ErrorCode = "sense.error.code";
    }
}
