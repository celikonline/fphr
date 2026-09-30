using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>Canlı monitör terminal satırı (Z5.3): çevrimiçi durumu, son görülme/okutma ve bugünkü okutma sayısı.</summary>
public sealed record LiveTerminalStatusDto(
    Guid TerminalId, string Code, string Name, string? GroupName, bool IsActive, bool Online,
    DateTime? LastSeenAtUtc, DateTime? LastPunchAtUtc, int TodayPunchCount);

[RequiresPermission("attendance.terminal.view")]
public sealed record GetLiveTerminalStatusQuery : IQuery<IReadOnlyList<LiveTerminalStatusDto>>;

public sealed class GetLiveTerminalStatusHandler(
    ITenantContext tenant, ITerminalRepository terminals, IPunchRepository punches, ITenantLocalClock clock)
    : IQueryHandler<GetLiveTerminalStatusQuery, IReadOnlyList<LiveTerminalStatusDto>>
{
    /// <summary>Terminal bu süre içinde görüldüyse çevrimiçi sayılır.</summary>
    public static readonly TimeSpan TerminalOnlineWindow = TimeSpan.FromMinutes(5);

    public async Task<Result<IReadOnlyList<LiveTerminalStatusDto>>> Handle(GetLiveTerminalStatusQuery query, CancellationToken cancellationToken)
    {
        var now = await clock.NowAsync(tenant.TenantId, cancellationToken);
        var today = DateOnly.FromDateTime(now.Local);

        var list = await terminals.ListTerminalsAsync(cancellationToken);
        var groups = (await terminals.ListGroupsAsync(cancellationToken)).ToDictionary(g => g.Id, g => g.Name);
        var todays = (await punches.ListForRangeAsync(tenant.TenantId, today, today, null, cancellationToken))
            .Where(p => !p.IsDeleted && p.TerminalId is not null)
            .GroupBy(p => p.TerminalId!.Value)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Last: g.Max(p => p.OccurredAtUtc)));

        IReadOnlyList<LiveTerminalStatusDto> rows = list
            .Select(t =>
            {
                todays.TryGetValue(t.Id, out var stats);
                return new LiveTerminalStatusDto(
                    t.Id, t.Code, t.Name, t.TerminalGroupId is { } g ? groups.GetValueOrDefault(g) : null, t.IsActive,
                    t.IsActive && t.LastSeenAtUtc is { } seen && now.Utc - seen < TerminalOnlineWindow,
                    t.LastSeenAtUtc, stats.Count == 0 ? null : stats.Last, stats.Count);
            })
            .ToList();
        return Result.Success(rows);
    }
}
