using Sense.Modules.Documents.Domain.DocumentTypes;

namespace Sense.Modules.Documents.Application.DocumentTypes;

public sealed record DocumentTypeDto(
    Guid Id,
    string Code,
    string Name,
    DocumentTypeStage Stage,
    bool IsRequired,
    DocumentRequirementCondition? Condition,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    bool IsSystem,
    bool IsActive,
    int SortOrder)
{
    public static DocumentTypeDto From(DocumentTypeDefinition d) => new(
        d.Id, d.Code, d.Name, d.Stage, d.IsRequired, d.Condition, d.RenewalPeriodDays, d.Quantity,
        d.IsSensitive, d.DefaultRetentionDays, d.IsSystem, d.IsActive, d.SortOrder);
}
