namespace Sense.Shared.Contracts.Bulk;

/// <summary>Toplu işlemde tek bir çalışanın sonucu.</summary>
public enum BulkItemOutcome
{
    Succeeded,
    Skipped,
    Failed,
}

/// <summary>Toplu işlemde bir çalışan için sonuç; <see cref="Code"/> atlanma/başarısızlık nedeninin hata kodudur.</summary>
public sealed record BulkItemResult(Guid EmployeeId, BulkItemOutcome Outcome, string? Code);

/// <summary>Seçim üzerinden yürütülen toplu işlemlerin ortak cevabı.</summary>
public sealed record BulkOperationResult(int Requested, int Succeeded, int Skipped, int Failed, IReadOnlyList<BulkItemResult> Items)
{
    public static BulkOperationResult From(int requested, IReadOnlyList<BulkItemResult> items) => new(
        requested,
        items.Count(i => i.Outcome == BulkItemOutcome.Succeeded),
        items.Count(i => i.Outcome == BulkItemOutcome.Skipped),
        items.Count(i => i.Outcome == BulkItemOutcome.Failed),
        items);
}
