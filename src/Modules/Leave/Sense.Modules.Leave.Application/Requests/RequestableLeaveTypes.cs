using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

public sealed record RequestableLeaveTypeDto(
    Guid Id, LeaveTypeCode Code, string Name, string? Description, LeaveUnit Unit, bool IsPaid, bool HasBalance, decimal? PerRequestMaxDays,
    bool RequiresDescription, bool RequiresDocument, bool CountsNonWorkingDays);

/// <summary>
/// "+ Talep": caller'ın kural setinin aktif, self-service türleri. `Name` ham haliyle döner (ör. "leave.type.annual");
/// API artık bunu ÇEVİRMİYOR (senseik-039) — çözümleme, kullanıcının uygulama içi dil tercihini baz alan frontend
/// i18n'e (`useLeaveName()`) bırakılıyor, tıpkı LeaveRuleSet.types[].name ve balance'lardaki leaveTypeName gibi.
/// </summary>
[RequiresPermission("leave.my.requests")]
public sealed record ListMyRequestableLeaveTypesQuery : IQuery<IReadOnlyList<RequestableLeaveTypeDto>>;

public sealed class ListMyRequestableLeaveTypesHandler(
    ICurrentUser currentUser, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope)
    : IQueryHandler<ListMyRequestableLeaveTypesQuery, IReadOnlyList<RequestableLeaveTypeDto>>
{
    public async Task<Result<IReadOnlyList<RequestableLeaveTypeDto>>> Handle(ListMyRequestableLeaveTypesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(employeeId, null, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        IReadOnlyList<RequestableLeaveTypeDto> types = loaded.Value.RuleSet.Types
            .Where(t => !t.IsDisabled && t.AllowSelfServiceRequest)
            .OrderBy(t => t.SortOrder)
            .Select(t => new RequestableLeaveTypeDto(
                t.Id, t.Code, t.Name, t.Description, t.Unit, t.IsPaid, t.HasBalance, t.LimitType == LeaveLimitType.PerRequest ? t.PerRequestMaxDays : null,
                t.RequiresDescription, t.RequiresDocument, t.CountsNonWorkingDays))
            .ToList();
        return Result.Success(types);
    }
}
