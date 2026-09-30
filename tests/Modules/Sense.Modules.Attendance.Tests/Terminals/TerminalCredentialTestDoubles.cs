using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Veri kapsamı: <see cref="Visible"/> null = şirket kapsamı; aksi halde yalnız bu çalışanlar. Sorulan izinler kaydedilir.</summary>
internal sealed class FakeTerminalDataScope : IDataScopeService
{
    public IReadOnlySet<Guid>? Visible { get; set; }

    public List<string> AskedPermissions { get; } = [];

    public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        AskedPermissions.Add(permission);
        return Task.FromResult(Visible);
    }

    public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default)
    {
        AskedPermissions.Add(permission);
        return Task.FromResult(Visible is null || Visible.Contains(employeeId));
    }

    public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
}

internal static class TerminalCredentialTestDoubles
{
    /// <summary>ExistsActiveAsync yalnız <paramref name="active"/> çalışanlar için true (işten ayrılmış/kaydı yok → false).</summary>
    public static IEmployeeLookup ActiveEmployees(params Guid[] active)
    {
        var set = active.ToHashSet();
        var lookup = Substitute.For<IEmployeeLookup>();
        lookup.ExistsActiveAsync(default, default, default).ReturnsForAnyArgs(call => set.Contains(call.ArgAt<Guid>(1)));
        return lookup;
    }

    public static ITerminalCredentialSaver Saver(Error? failure = null)
    {
        var saver = Substitute.For<ITerminalCredentialSaver>();
        saver.SaveAsync(default, default).ReturnsForAnyArgs(failure is null ? Result.Success() : Result.Failure(failure));
        return saver;
    }
}
