using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// Kimlik değişikliklerini kaydeder; aynı (kiracı, tür, özet) eşzamanlı eklendiğinde tekil indeks ihlalini 500 yerine ekleme kuralının
/// çakışma hatasına çevirir (<see cref="TerminalCredentialRules.InUseBySomeoneElse"/>: kart/harici in_use, PIN genel rejected).
/// </summary>
public interface ITerminalCredentialSaver
{
    Task<Result> SaveAsync(TerminalCredentialKind kind, CancellationToken cancellationToken);
}
