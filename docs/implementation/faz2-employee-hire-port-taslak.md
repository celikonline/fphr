# Faz 2 — Employee.Contracts hire köprüsü taslağı (K-11)

**Durum:** Taslak, kod yazılmadı. PO onayı bekliyor (PANO K-11: "faz-02 BE taslak yazıyor, PO onayı sonraki tur").
İhtiyaç sahibi Faz 9 (Recruitment) — bir aday işe alındığında (`JobApplication` "Hired" aşamasına geçince)
Employee modülünde gerçek bir çalışan kaydı oluşturması gerekiyor, ama Recruitment'ın Employee.Domain'e
bağımlılığı olamaz (modüller yalnızca `*.Contracts` üzerinden konuşur — CLAUDE.md).

## Önerilen şekil

```csharp
// Sense.Modules.Employee.Contracts
public interface IEmployeeHirePort
{
    /// <summary>Idempotent: aynı CandidateId ile ikinci çağrı yeni kayıt açmaz, mevcut EmployeeId'yi döner
    /// (aday iki kez "Hired" işaretlenirse, veya olay iki kez işlenirse çift kayıt oluşmasın diye).</summary>
    Task<Result<Guid>> HireFromCandidateAsync(HireFromCandidateRequest request, CancellationToken cancellationToken = default);
}

public sealed record HireFromCandidateRequest(
    Guid TenantId,
    Guid CandidateId,          // idempotency anahtarı
    string FirstName,
    string LastName,
    string? PersonalEmail,     // Candidate.Email -> Employee.PersonalEmail (WorkEmail burada yok, işe alım akışında henüz atanmamış olabilir)
    string? Phone,
    DateOnly HireDate,
    Guid? OrgUnitId,           // JobRequisition'ın bağlı olduğu birim
    Guid? ManagerEmployeeId,
    Guid ActorUserId);
```

## Tasarım kararları / açık sorular

1. **Idempotency:** `CandidateId` anahtar. `Employee` aggregate'ine `SourceCandidateId (Guid?, nullable, unique index where not null)`
   eklenmesi gerekecek — bu **additive bir migration**, ayrı MIGRATION-ISTEGI. `HireFromCandidateAsync` önce
   bu alanla mevcut kaydı arar, varsa `EmployeeId`'sini döner (yeni kayıt açmaz).
2. **Zorunlu alanlar:** `FirstName`, `LastName`, `HireDate` zorunlu (Employee.Create ile aynı kural).
   `NationalId`/`WorkEmail` bu aşamada yok — Draft benzeri bir durumda oluşturulup İK sonradan tamamlayacak
   (bkz. `CreateAsync(..., asDraft: true)` kalıbı, `EmployeeIntegrationTests.cs`'te zaten var).
3. **Durum:** Yeni kayıt `EmployeeStatus.Draft` mi yoksa doğrudan `Active` mi başlar? Öneri: `Draft`,
   HireDate ileri tarihliyse (henüz başlamadıysa) — mevcut `UpdateEmployeeHandler`'daki
   "wasHired -> Active" geçiş kuralı zaten bunu destekliyor. PO/Faz 9 ile netleşmeli.
4. **Kim çağırır:** Recruitment'ın `JobApplicationStageChanged` (Contracts.Events, mevcut) tüketicisi mi,
   yoksa yeni bir komut mu (`HireCandidateCommand` Recruitment tarafında, senkron olarak
   `IEmployeeHirePort` çağırır)? Öneri: senkron komut - aday işe alma kararı geri alınabilir bir iş akışı
   değil, HTTP isteği sırasında hemen sonuç (yeni EmployeeId) dönmesi UX açısından daha iyi. Event-tabanlı
   olursa Recruitment tarafı "hangi EmployeeId oluştu" bilgisini nereden okuyacağını da tasarlamalı.
5. **Yetki:** `IEmployeeHirePort` bir Contracts arayüzü olduğu için `[RequiresPermission]` uygulanmaz (o,
   ICommand/IQuery işaretlemesi) — çağıran taraf (Recruitment) kendi yetkisini (`recruitment.applications.hire`
   gibi) kontrol eder; Employee tarafı sadece veriyi yazar. Bu, mevcut `ILocationLookup`/`IOrgUnitLookup`
   gibi salt-okunur portlardan farklı - **yazma** yapan ilk cross-module port olacak, hata/rollback
   davranışı (transaction sınırı Employee'nin kendi DbContext'inde mi kalır, yoksa distributed olur mu)
   ayrıca netleşmeli.

## Bu turda yapılmayan
Kod, migration, DI kaydı — hepsi PO onayından sonra, Faz 9 ile birlikte tek turda tasarlanacak
("tasarımı birlikte yapılacak, tek taraflı yazma" — TALIMAT tur 1 notu).
