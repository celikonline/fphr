using Sense.Modules.Organization.Domain.CostCenters;

namespace Sense.Modules.Organization.Application.CostCenters;

/// <summary>Cost center shape returned to the web Cost Centers screen (S-4, roadmap Faz 2 item #2).</summary>
public sealed record CostCenterDto(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Create response - S-4 sözleşmesi yalnız yeni kaydın id'sini döner, tam DTO değil.</summary>
public sealed record CreateCostCenterResult(Guid Id);

internal static class CostCenterMapper
{
    public static CostCenterDto ToDto(CostCenter c) => new(c.Id, c.Code, c.Name, c.IsActive);
}
