# Faz 4 Durum Takibi

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 4 satırları (kalem #62–#74, + #41/#44/#45'in Faz 4'e düşen tüketici tarafı); ayrıntı [11.3.09-dosya-belge-kvkk-vize.md](./11.3.09-dosya-belge-kvkk-vize.md) (bu dosyadaki D1 kalemi için).

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

**Not (2026-09-16):** Bu oturumda çalışma ağacında eşzamanlı başka oturum(lar)ın da yazdığı çok sayıda commit edilmemiş değişiklik bulundu (yeni `src/Modules/Attendance`, `Payroll`, `Recruitment`, `Analytics`, Employee E4/E5a/Terminations çalışması, Leave `LegislationParameter` genişletmesi, web navigasyon/ana sayfa değişiklikleri, yeni `faz3-durum-takibi.md`/`faz7-durum-takibi.md`) — bu dosya yalnızca aşağıdaki D1 kalemini (Documents modülü) izler, diğer eşzamanlı işler kendi durum dosyalarında izlenmeli.

## Faz 4 kalemleri

| # | Kalem | Paket | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|---|
| 66–68 | Zorunlu belge listesi seed'i (5 yaşam döngüsü aşaması), koşullu zorunlu belge kuralları (`DocumentRequirementRule`), sistem kayıtlarının özlük dosyasına belge olarak düşmesi | D1 | 🟡 Kısmi | Bkz. aşağıdaki "D1" bölümü | Sistem kayıtlarının otomatik belge üretimi (#68 — izin formu/FM onayı/imzalı pusula PDF/Word render + `SourceRef`) yapılmadı; yalnız taksonomi + koşullu değerlendirme + manuel karşılama var |
| 62 | Harcama: çoklu fiş + vergi oranı + mobil OCR | — | ❌ Başlanmadı | — | `Expense` modülü henüz yok |
| 63 | Avans talebi: para birimi, taksit | — | ❌ Başlanmadı | — | `Expense` modülü henüz yok |
| 64 | "Kendi hesabım > Ödemeler" birleşik ekran | — | ❌ Başlanmadı | — | Expense'e bağlı |
| 65 | Zimmet şablonu | — | ❌ Başlanmadı | — | `Assets` modülü henüz yok |
| 69 | Yardım Merkezi + Desteğe bağlan (Announcements > Support) | — | ❌ Başlanmadı | — | `Announcements` modülü henüz yok |
| 70 | Raporlar menüsü + "Kendin oluştur" + asenkron e-posta | — | ❌ Başlanmadı | — | — |
| 71 | Etkinlik takvimi widget'ı | — | ❌ Başlanmadı | — | Announcements'a bağlı |
| 72 | Ön koşul eğitim + mentör atama | — | ❌ Başlanmadı | — | — |
| 73 | Takdir/teşekkür + yatay pozisyon değişikliği akış öğesi | — | ❌ Başlanmadı | — | — |
| 74 | Onboarding modülü karar noktası | — | ❌ Başlanmadı | — | Faz 4 sonu kararı bekliyor |

## D1 — Documents: belge tipi taksonomisi + koşullu gereksinim değerlendirici (yalnız backend)

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı).

**Kapsam:** `DocumentTypeDefinition` (5 aşamalı taksonomi: Onboarding/Conditional/DuringEmployment/Offboarding/Recommended; tenant başına kopyalanan, düzenlenebilir sistem seed'i — 43 satır, `LeaveType` ile aynı desen), `DocumentRequirementCondition` (12 koşul türü: yaş<18, engellilik, yabancı uyruk, teşvik, engelli vergi indirimi, eski hükümlü/terör mağduru, periyodik sağlık kontrolü, ehliyet gereksinimi, açık zimmet, ağır/tehlikeli iş, FM onayı, vardiyalı çalışma), saf domain servisi `DocumentRequirementEvaluator.ResolveAtHire` (yalnız Onboarding + eşleşen Conditional; DuringEmployment/Offboarding/Recommended işe alımda tetiklenmiyor — olay/sihirbaz güdümlü, kapsam dışı), `EmployeeDocumentRequirement` (çalışan × belge tipi; Missing/Uploaded/Waived, muafiyet nedeni/kim/ne zaman, `Document`'e bağlantı).

**Tetikleyici:** `EmployeeHired` (Employee.Contracts) tüketicisi `EmployeeHiredDocumentRequirementHandler` — kiracının kataloğunu tembel (lazy) seed'ler, `Employee.Contracts.IEmployeeDocumentEligibilityLookup` ile yalnız türetilmiş booleanları (`IsUnder18`, `HasDisability` — ham `BirthDate`/`DisabilityDegree` asla modül sınırını geçmiyor) okuyup geçerli gereksinimleri materialize ediyor; `DueDate = HireDate + 30 gün` (sabit; kiracı bazlı `Documents.MissingDocument.GraceDays` ayarı yapılmadı). Manuel `ReevaluateEmployeeDocumentRequirementsCommand` aynı mantığı mevcut bir çalışan için yeniden çalıştırıyor (HireDate'e erişimi yok, bugünün tarihini taban alıyor — küçük bir tutarsızlık, not düşüldü).

**API:** `documents/types` (liste/oluştur/güncelle/aktifleştir/pasifleştir), `documents/employees/{id}/requirements`, `documents/me/requirements`, `documents/requirements/report` (sayfalı, durum filtresi), `documents/requirements/{id}/{waive,clear-waive,link}`, `documents/employees/{id}/requirements/reevaluate`. İzinler: `documents.document_types.{view,manage}`, `documents.requirements.{view,manage}`, `documents.my.requirements` — mevcut joker izin desenleri (`documents.*`, `documents.*.view`, `*.my.*`) tüm rollere zaten ulaşıyor, `SystemRoleDefinitions.cs`'ye dokunulmadı.

**Doğrulama:** `Sense.Modules.Documents.Tests` (yeni proje) 42/42 test geçti (domain: `DocumentTypeDefinition`/`EmployeeDocumentRequirement`/`DocumentRequirementEvaluator`; application: Create/Update/List/Waive/ClearWaive handler'ları + `EmployeeHiredDocumentRequirementHandler` — eşleşen/eşleşmeyen koşul + idempotency). `dotnet build Sense.slnx` bu değişiklikler için 0 hata verdi. Tüm çözümün `dotnet test`'i eşzamanlı diğer oturumların etkisiyle bu oturumda tamamlanamadı — bir dahaki fırsatta tekrar çalıştırılıp diğer modüllerde (Attendance/Payroll/Recruitment/Analytics/Terminations) regresyon olmadığı doğrulanmalı.

**Eksik / Not:**
- Web arayüzü yok (Ayarlar > Belge tipleri sayfası, çalışan kartı "Dosyalar" sekmesi, Eksik belge raporu sayfası) — sonraki paket (D2).
- #68'in "sistem kayıtları belge olarak düşer" kısmı yapılmadı: `IDocumentRenderer` (PDF/Word), `Document.SourceRef`/`SignatureStatus` alanları, Leave/Workflow olay tüketicileri (izin formu, FM onay belgesi vb.) — Faz 1'in "İzin formu PDF ve Word" kalemiyle birlikte ele alınmalı, ayrı paket.
- Test projesi Postgres/Testcontainers entegrasyon testi değil, NSubstitute mock testleri — modülün geri kalanının `[Collection("PostgreSQL")]` deseniyle tutarlı değil; takip gerekiyor.
- Koşullardan yalnız 2'si (`AgeUnder18`, `HasDisability`) gerçek veriyle değerlendiriliyor; kalan 10'u (`NonTurkishNational`, `HasIncentive`, `UsesDisabilityTaxReduction`, `ExConvictOrTerrorVictim`, `RequiresPeriodicHealthCheck`, `RequiresDriving`, `HasOpenAssets`, `VeryHazardousJob`, `HasOvertimeConsent`, `IsShiftWorker`) ilgili modüller (Employee Nationality alanı, Payroll teşvik, Assets, Attendance, Leave FM onayı) gelene kadar `false` sabit — kod içinde açıkça not düşüldü.
- Grace period (30 gün) ve reevaluate komutunun tarih tabanı sabit kodlanmış; tenant ayarı değil.
- Kod henüz commit edilmedi.

## Değişiklik geçmişi

- 2026-09-16: Dosya oluşturuldu; D1 (Documents belge tipi taksonomisi + koşullu gereksinim değerlendirici, backend) tamamlandı.
