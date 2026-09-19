using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;

namespace Sense.Modules.OccupationalSafety.Application.HazardClassifications;

public sealed record HazardClassificationDto(Guid Id, Guid LocationId, HazardClass HazardClass, DateOnly EffectiveFrom, string? Note);

internal static class HazardClassificationMapper
{
    public static HazardClassificationDto ToDto(WorkplaceHazardClassification classification) =>
        new(classification.Id, classification.LocationId, classification.HazardClass, classification.EffectiveFrom, classification.Note);
}
