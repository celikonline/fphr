using Sense.Modules.Employee.Domain.Employees;

namespace Sense.Modules.Employee.Domain.Terminations;

/// <summary>
/// SGK "işten ayrılış nedeni" kod ailesi eşlemesi (5510 sayılı Kanun; doğrulanmış tablo, bkz.
/// docs/handoffs/sgk-exit-codes.md §1 - senseik-006/F-01). Tek kaynak: hem katalog düzenleme zamanı
/// (Application.Terminations.TerminationReasonCommands validator'ları) hem fesih zamanı
/// (Application.Employees.TerminateEmployeeHandler) buradan okur, böylece iki tüketici asla birbirinden sapmaz.
/// </summary>
public static class SgkExitCodeRules
{
    /// <summary>
    /// İzin verilen kod kümesi bu fesih türü için; null = tür herhangi bir kısıtlama getirmiyor
    /// (<see cref="TerminationType.Other"/> - "diğer nedenler" havuzu, SGK'da tek bir karşılığı yok).
    /// </summary>
    public static IReadOnlySet<string>? AllowedCodes(TerminationType type) => type switch
    {
        TerminationType.ValidCause_Art18 => Set("04"),
        TerminationType.EmployeeJustCause_Art24 => Set("23", "24", "25"),
        TerminationType.EmployerJustCause_Art25 => Set("26", "27", "28"),
        TerminationType.MutualRescission => Set("22"),
        TerminationType.Resignation => Set("03"),
        TerminationType.FixedTermExpiry => Set("05"),
        TerminationType.Other => null,
        _ => null,
    };

    /// <summary>
    /// Katalog zamanı tutarlılığı (nedeni tanımlarken/düzenlerken): kod boş bırakılabilir - sistem seed'i
    /// md.24/md.25 aileleri için bilerek bir alt-kod varsaymıyor (bkz. <see cref="TerminationReasonSeed"/>).
    /// Ancak bir kod verilmişse, türün izinli ailesinde olmalı.
    /// </summary>
    public static bool IsCatalogConsistent(TerminationType? type, string? sgkExitCode)
    {
        var trimmed = Normalize(sgkExitCode);
        if (type is null || trimmed is null)
        {
            return true;
        }

        var allowed = AllowedCodes(type.Value);
        return allowed is null || allowed.Contains(trimmed);
    }

    /// <summary>
    /// Fesih zamanı tutarlılığı (gerçek kayıt, <see cref="Employees.Employee.Terminate"/> öncesi): çift eksiksiz
    /// olmalı. Birden fazla geçerli kodu olan bir aile (md.24/md.25) için güvenli bir varsayılan yoktur - çağıran
    /// (sihirbaz veya doğrudan API çağrısı) açıkça bir alt-kod seçmelidir.
    /// </summary>
    public static bool IsTerminationConsistent(TerminationType? type, string? sgkExitCode)
    {
        if (type is null)
        {
            return true;
        }

        var allowed = AllowedCodes(type.Value);
        if (allowed is null)
        {
            return true;
        }

        var trimmed = Normalize(sgkExitCode);
        return trimmed is null ? allowed.Count <= 1 : allowed.Contains(trimmed);
    }

    private static string? Normalize(string? sgkExitCode) =>
        string.IsNullOrWhiteSpace(sgkExitCode) ? null : sgkExitCode.Trim();

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}
