using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Infrastructure.Services;

/// <summary>
/// <see cref="IIsolatedApprovalDecisionRunner"/>: a child DI scope per decision gives the command its own
/// WorkflowDbContext (so its UnitOfWork opens its own transaction instead of joining the caller's) and its own outbox.
/// The caller's identity is carried into the child scope explicitly - <see cref="CurrentUserAccessor"/> is the only
/// per-scope request state; tenant, audit source and correlation id are AsyncLocal and flow on their own. An unexpected
/// exception is logged and reported as that task's failure so the remaining tasks still run.
/// </summary>
public sealed partial class IsolatedApprovalDecisionRunner(
    IServiceScopeFactory scopes, ICurrentUser currentUser, ILogger<IsolatedApprovalDecisionRunner> logger) : IIsolatedApprovalDecisionRunner
{
    public async Task<Result> DecideAsync(DecideApprovalTaskCommand command, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().Set(currentUser);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        try
        {
            return await dispatcher.Send(command, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.DecisionFailed(logger, ex, command.TaskId);
            return Error.Failure(ErrorCodes.InternalError);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4400, Level = LogLevel.Error, Message = "Bulk approval decision failed for task {TaskId}")]
        public static partial void DecisionFailed(ILogger logger, Exception exception, Guid taskId);
    }
}
