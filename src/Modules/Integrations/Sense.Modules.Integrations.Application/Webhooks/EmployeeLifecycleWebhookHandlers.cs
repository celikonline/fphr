using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Integrations.Application.Webhooks;

/// <summary>
/// Turns Employee's outbox events into outbound webhook deliveries (Faz 8 #61 - the "Bordro modülü kapalıyken
/// employee.* event senkronu" half of the external payroll export item; the full personnel/timesheet/pay-item
/// export payload is a separate, Payroll-module-dependent item - see faz8-durum-takibi.md). Payloads carry only what
/// the source event carries: no salary amounts or IBANs, by the same design the events themselves follow.
/// </summary>
public sealed class EmployeeLifecycleWebhookHandlers(WebhookDispatchService dispatch) :
    IIntegrationEventHandler<EmployeeHired>,
    IIntegrationEventHandler<EmployeeTerminated>,
    IIntegrationEventHandler<EmployeeRehired>,
    IIntegrationEventHandler<EmployeeSuspended>,
    IIntegrationEventHandler<EmployeeReactivated>,
    IIntegrationEventHandler<ManagerChanged>,
    IIntegrationEventHandler<CareerChanged>,
    IIntegrationEventHandler<SalaryChanged>,
    IIntegrationEventHandler<EmployeeProfileChanged>
{
    public Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeHired,
            new { integrationEvent.EmployeeId, integrationEvent.HireDate, integrationEvent.LeaveAccrualStartDate },
            cancellationToken);

    public Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeTerminated,
            new
            {
                integrationEvent.EmployeeId,
                TerminationType = integrationEvent.TerminationType.ToString(),
                integrationEvent.ReasonCode,
                integrationEvent.SgkExitCode,
            },
            cancellationToken);

    public Task Handle(EmployeeRehired integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeRehired,
            new
            {
                integrationEvent.EmployeeId,
                integrationEvent.HireDate,
                integrationEvent.LeaveAccrualStartDate,
                integrationEvent.SeniorityStartDate,
            },
            cancellationToken);

    public Task Handle(EmployeeSuspended integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeSuspended,
            new { integrationEvent.EmployeeId, Reason = integrationEvent.Reason.ToString() },
            cancellationToken);

    public Task Handle(EmployeeReactivated integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(WebhookEventTypes.EmployeeReactivated, new { integrationEvent.EmployeeId }, cancellationToken);

    public Task Handle(ManagerChanged integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeManagerChanged,
            new
            {
                integrationEvent.EmployeeId,
                integrationEvent.OldManagerEmployeeId,
                integrationEvent.NewManagerEmployeeId,
                integrationEvent.EffectiveFrom,
            },
            cancellationToken);

    public Task Handle(CareerChanged integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeCareerChanged,
            new
            {
                integrationEvent.EmployeeId,
                integrationEvent.CareerRecordId,
                ChangeType = integrationEvent.ChangeType.ToString(),
                integrationEvent.ValidFrom,
            },
            cancellationToken);

    public Task Handle(SalaryChanged integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeSalaryChanged,
            new { integrationEvent.EmployeeId, integrationEvent.SalaryRecordId, integrationEvent.EffectiveFrom },
            cancellationToken);

    public Task Handle(EmployeeProfileChanged integrationEvent, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(
            WebhookEventTypes.EmployeeProfileChanged,
            new { integrationEvent.EmployeeId, integrationEvent.Section },
            cancellationToken);
}
