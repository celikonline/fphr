using System.Text.Json;
using FluentValidation;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>
/// Requests a report (Kolay İK: rapor türü + Birimler/Ay/Yıl filtreleri → oluştur). Runs synchronously today (R0 - see
/// ReportExecution's remark); the state machine it drives (Queued -&gt; Running -&gt; Succeeded/Failed) is the same
/// one a future async path would use, so moving generation onto Hangfire later only changes who calls
/// Start/Complete/Fail, not the aggregate or this DTO shape.
/// </summary>
[RequiresPermission("reports.executions.generate")]
public sealed record RequestReportCommand(string ReportKey, Dictionary<string, string>? Parameters) : ICommand<ReportExecutionDto>;

public sealed class RequestReportValidator : AbstractValidator<RequestReportCommand>
{
    public RequestReportValidator() => RuleFor(x => x.ReportKey).NotEmpty();
}

public sealed class RequestReportHandler(
    IEnumerable<IReportProvider> providers,
    IReportExecutionRepository executions,
    IReportMailer mailer,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IPermissionService permissions,
    TimeProvider clock) : ICommandHandler<RequestReportCommand, ReportExecutionDto>
{
    public async Task<Result<ReportExecutionDto>> Handle(RequestReportCommand command, CancellationToken cancellationToken)
    {
        var provider = providers.FirstOrDefault(p => p.Key == command.ReportKey);
        if (provider is null)
        {
            return Error.Validation(ReportingErrors.UnknownReportKey);
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        if (provider.RequiredPermission is not null && !await permissions.HasAsync(userId, provider.RequiredPermission, cancellationToken))
        {
            return Error.Forbidden(ReportingErrors.ReportPermissionDenied);
        }

        // Blank values are dropped before validation so an empty form field never counts as "present but malformed".
        var parameters = (command.Parameters ?? [])
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);

        var validation = ReportParameterValidation.Validate(provider.Parameters, parameters);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var parametersJson = parameters.Count > 0 ? JsonSerializer.Serialize(parameters) : null;
        var created = ReportExecution.Create(tenant.TenantId, command.ReportKey, parametersJson, userId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var execution = created.Value;
        executions.Add(execution);
        execution.Start(now);

        // A provider is third-party (another module's code) from this handler's point of view; a failure there
        // becomes a Failed execution (visible to the caller via polling/history) rather than a 500 on this request -
        // that's the whole point of the Queued/Running/Succeeded/Failed state machine existing at all.
        try
        {
            var file = await provider.GenerateAsync(tenant.TenantId, parameters, cancellationToken);
            execution.Complete(file.FileName, file.ContentType, file.Content, clock.GetUtcNow().UtcDateTime);

            if (currentUser.Email is { Length: > 0 } email)
            {
                await mailer.SendReportReadyAsync(email, currentUser.DisplayName, execution.Id, execution.ReportKey, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            execution.Fail(ex.Message, clock.GetUtcNow().UtcDateTime);
        }

        return ReportExecutionDto.From(execution);
    }
}
