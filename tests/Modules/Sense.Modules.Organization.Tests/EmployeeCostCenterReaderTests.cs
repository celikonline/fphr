using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

public sealed class EmployeeCostCenterReaderTests
{
    [Fact]
    public async Task ForwardsTenantEmployeesCancellationAndRealAssignments()
    {
        var lookup = Substitute.For<IEmployeeCostCenterLookup>();
        var tenantId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        IReadOnlyDictionary<Guid, Guid> mappings = new Dictionary<Guid, Guid> { [ids[0]] = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource();
        lookup.GetAsync(tenantId, ids, cancellation.Token).Returns(mappings);
        var reader = new EmployeeCostCenterReader(lookup);

        var result = await reader.GetEmployeeCostCenterMappingAsync(tenantId, ids, cancellation.Token);

        result.ShouldBeSameAs(mappings);
        await lookup.Received(1).GetAsync(tenantId, ids, cancellation.Token);
    }
}
