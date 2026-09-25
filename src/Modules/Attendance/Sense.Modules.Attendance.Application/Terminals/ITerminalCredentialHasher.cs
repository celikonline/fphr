using Sense.Modules.Attendance.Domain.Terminals;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// Terminal kimliği (kart/PIN/dış kimlik) → saklanan/aranan değer (Z5.1/Z5.4). Deterministik HMAC-SHA256 özeti (sunucu tarafı pepper,
/// yapılandırma <c>Attendance:Terminals:CredentialPepper</c>); girdiye kiracı ve tür katılır, böylece aynı PIN farklı kiracılarda farklı
/// özet üretir. <see cref="EmployeeTerminalCredential.Value"/> yalnız bu özeti taşır - kimlik oluşturan/içe aktaran her yol (T2) ve okutma
/// ucu aynı uygulamayı kullanmalıdır. Normalleştirme: baş/son boşluk atılır; kart numarası büyük harfe çevrilir (okuyucular hex UID'yi
/// farklı harf büyüklüğüyle gönderebilir); PIN ve dış kimlik olduğu gibi kalır.
/// </summary>
public interface ITerminalCredentialHasher
{
    string Hash(Guid tenantId, TerminalCredentialKind kind, string rawValue);
}
