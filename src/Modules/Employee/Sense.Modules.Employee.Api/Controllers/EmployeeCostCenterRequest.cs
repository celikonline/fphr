namespace Sense.Modules.Employee.Api.Controllers;

public sealed record EmployeeCostCenterRequest
{
    // Explicit null clears the assignment; a missing property is rejected by JSON deserialization.
    public required Guid? CostCenterId { get; init; }
}
