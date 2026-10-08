using System.Text.Json;
using System.Globalization;
using Sense.Shared.Contracts.Events;
using FluentValidation;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>
/// Requests a report (Kolay İK: rapor türü + filters → generate). Async providers return a queued execution; sync
/// providers complete it in the request. The existing state machine drives both paths.
/// <c>Notify=false</c> (the on-screen report page) skips the 'report ready' e-mail — the caller is already looking at the result.
/// </summary>
[RequiresPermission("reports.executions.generate")]
public sealed record RequestReportCommand(string ReportKey, Dictionary<string, string>? Parameters, bool Notify = true) : ICommand<ReportExecutionDto>;

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
    TimeProvider clock,
    IIntegrationEventOutbox? outbox = null,
    IDataScopeService? dataScope = null,
    IEnumerable<ICustomReportFieldContributor>? customContributors = null) : ICommandHandler<RequestReportCommand, ReportExecutionDto>
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

        if (provider.Key == "report.custom")
        {
            var customValidation = await ValidateCustomReportAsync(parameters, userId, cancellationToken);
            if (customValidation.IsFailure)
            {
                return customValidation.Error;
            }
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
        if (provider.ExecutionMode == "async")
        {
            if (userId == Guid.Empty || outbox is null || dataScope is null)
            {
                return Error.Forbidden(ReportingErrors.ReportPermissionDenied);
            }

            var visible = provider.DataScopePermission is { } permission
                ? await dataScope.GetVisibleEmployeeIdsAsync(permission, cancellationToken)
                : null;
            var sensitive = await permissions.HasAsync(userId, "employee.sensitive.view", cancellationToken);
            outbox.Enqueue(new ReportGenerationRequested(tenant.TenantId, execution.Id, userId, currentUser.EmployeeId,
                currentUser.Email, currentUser.DisplayName, CultureInfo.CurrentUICulture.Name, command.Notify, visible?.ToList(), sensitive));
            return ReportExecutionDto.From(execution);
        }

        execution.Start(now);

        // A provider is third-party (another module's code) from this handler's point of view; a failure there
        // becomes a Failed execution (visible to the caller via polling/history) rather than a 500 on this request -
        // that's the whole point of the Queued/Running/Succeeded/Failed state machine existing at all.
        try
        {
            var file = await provider.GenerateAsync(tenant.TenantId, parameters, cancellationToken);
            execution.Complete(file.FileName, file.ContentType, file.Content, clock.GetUtcNow().UtcDateTime);

            if (command.Notify && currentUser.Email is { Length: > 0 } email)
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

    private async Task<Result> ValidateCustomReportAsync(Dictionary<string, string> parameters, Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || !await permissions.HasAsync(userId, ReportingPermissions.CustomBuild.Key, cancellationToken))
        {
            return Error.Forbidden(ReportingErrors.ReportPermissionDenied);
        }

        if (!parameters.TryGetValue("fieldKeys", out var serializedKeys))
        {
            return Error.Validation(CustomReportErrors.NoFieldsSelected);
        }

        string[] fieldKeys;
        try
        {
            fieldKeys = JsonSerializer.Deserialize<string[]>(serializedKeys) ?? [];
        }
        catch (JsonException)
        {
            return Error.Validation(CustomReportErrors.UnknownField);
        }

        var contributors = customContributors ?? [];
        var fields = new List<CustomReportFieldDescriptor>();
        foreach (var contributor in contributors)
        {
            fields.AddRange(await contributor.GetFieldsAsync(tenant.TenantId, cancellationToken));
        }

        var currentPermissions = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var availableFields = CustomReportFieldCatalog.Filter(fields, currentPermissions);
        var from = ReportParameters.GetDate(parameters, ReportParameterKeys.From);
        var to = ReportParameters.GetDate(parameters, ReportParameterKeys.To);
        var selected = CustomReportSelectionValidator.Validate(availableFields, fieldKeys, currentPermissions, from, to);
        if (!selected.IsValid)
        {
            return Error.Validation(selected.ErrorCode!);
        }

        if (!ReportParameters.TryGetGuidList(parameters, ReportParameterKeys.OrgUnitIds, out var units))
        {
            return Error.Validation(CustomReportErrors.InvalidOrgUnits);
        }

        if (units.Count > 0 && dataScope is not null)
        {
            foreach (var permission in selected.Fields.Select(field => field.ScopePermission).Where(value => value is not null).Distinct(StringComparer.Ordinal))
            {
                var accessible = await dataScope.GetAccessibleOrgUnitIdsAsync(permission!, cancellationToken);
                if (accessible is not null && units.Any(unit => !accessible.Contains(unit)))
                {
                    return Error.Validation(CustomReportErrors.InvalidOrgUnits);
                }
            }
        }

        return Result.Success();
    }
}
