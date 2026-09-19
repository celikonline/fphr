## Merge Gate Durumu (doğrulama turu, 2026-09-19)

- Build: `Sense.Modules.Employee.Tests` + `Sense.Modules.Leave.Tests` (bağımlı tüm src projeleri) native `dotnet build` temiz.
- `dotnet test tests/Modules/Sense.Modules.Employee.Tests` (tam, Testcontainers dahil): **224/228** — 4 hata
  `PaymentsIntegrationTests` (`IUserProfileLookup` DI eksikliği, dev'de de var, bu kartla ilgisiz). Yeni
  `HireDateCorrection_OnHiredEmployee_RaisesEmploymentDatesChanged_WithFollowingAccrualStart` dahil geri kalan hepsi yeşil.
- Unit (container'sız) koşu: Employee **174/174**, Leave **107/107** (yeni `EmployeeEmploymentDatesChangedHandlerTests` dahil).
- Leave entegrasyon testleri: Docker Desktop bu oturumda yanıt vermez hale geldiği için **tamamlanamadı** — yeni
  handler container gerektirmiyor (unit testlerle kapsandı); merge sonrası bir Leave.Tests tam koşusu önerilir.
- Web: `pnpm exec tsc --noEmit` temiz; `pnpm exec eslint src` 0 hata (1 önceden var olan uyarı, `employee-list.tsx`).
- Migration: yok.

## Sonraki Tur İçin Öneriler

1. Leave.Tests tam koşusu (Testcontainers) — Docker sağlıklıyken.
2. `EmployeeImportRowHandler` tarih değişikliklerinde `EmployeeEmploymentDatesChanged` atsın; `EmployeeRehired` için Leave handler'ı.
3. Mevcut drift'li Leave profilleri için tek seferlik yeniden senkron.
4. §4.1'deki Türkçe karakterli e-posta geriye dönük düzeltme script'i (dry-run önce, HR onayı sonra).
5. (dev'de bulunan, bu kartla ilgisiz) `UpdateEmployeeCommand` `SicilNumarasi/Nationality/ForeignIdNumber/PassportNumber/SgkOccupationCode`
   taşımıyor → web'den her çalışan kaydında bu alanlar null'a çekiliyor olabilir; ayrı kart olarak incelenmeli.
