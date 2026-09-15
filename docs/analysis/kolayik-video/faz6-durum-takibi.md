# Faz 6 Durum Takibi (ATS / İşe Alım — Recruitment modülü)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 6 satırları (#82–96), ayrıntı [11.3.11-ats-ishe-alim.md](./11.3.11-ats-ishe-alim.md); iş bölümü [paralel-gelistirme-plani.md](../../implementation/paralel-gelistirme-plani.md).

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

## Sıralama kararı

`paralel-gelistirme-plani.md` §7, Recruitment'ı "teknik olarak bağımsız (yalnız Identity rolleri + Organization `Location`/`OrgUnit` gerekir, ikisi de mevcut) ama yol haritası Faz 5 ile paralel öneriyor — şimdi önerilmez" olarak işaretliyor. Kullanıcı kararıyla Faz 1a bitmeden Faz 6'ya bilinçli olarak geçildi (2026-09-16); Faz 2–5 bu hat için beklemede değil, yalnız Recruitment bu sırayı beklemeden başladı.

## Paket sırası (Employee modülünün E0–E7 yaklaşımıyla aynı desen)

| Paket | Kapsam | Durum |
|---|---|---|
| **R0** | Modül iskeleti (5 proje + migration + hot-file kayıtları), `JobPosting` (+ `JobPostingAccess`) yaşam döngüsü (taslak→yayınla→kapat→arşivle), `Candidate` (elle ekleme), `JobApplication` (sabit aşama seti, elle taşıma), temel web ekranları | ✅ Bitti |
| R1 | `JobRequisition` + onay akışı (Workflow'a bağlı; `Organization.Contracts.ApprovalRequestTypes` genişletmesi gerekir — contract-first PR), `IEmploymentTypeLookup` (Organization.Contracts'a eklenmeli) | ❌ Başlanmadı |
| R2 | Form builder (`RecruitmentFormTemplate`: Feedback/Scorecard/Outcome), `AutomationRule` kural motoru (aşama geçişinde zorunlu form vb.), etiketleme (`Tag`/`CandidateTag`) | ❌ Başlanmadı |
| R3 | Kariyer sitesi + herkese açık başvuru formu (üç katman) + Open Graph, e-posta şablonları (`EmailTemplate`/`CandidateEmail`) | ❌ Başlanmadı |
| R4 | Self-scheduling link (`SchedulingLink`) + takvim entegrasyonu | ❌ Başlanmadı |
| R5 | ATS raporları (Overview/Daily counts/export), `RecruitmentMetricSnapshot` | ❌ Başlanmadı |
| R6 (karar bekliyor) | AI ön eleme botu (`ICandidateScreeningBot`), Chrome eklentisi + kaynak kullanımı kotası, referral programı, hire→Employee devri (`CreateFromCandidate`, `Employee.Contracts`'ta henüz yok) | ❌ Başlanmadı / roadmap karar noktaları |

## R0 kalemleri (#82–96 eşlemesi)

| # | Roadmap kalemi | Durum | Kanıt / Not |
|---|---|---|---|
| 82 | ATS ayrı hesap → tek kiracıda modül; Konum/Takım/Çalışma tipi eşlemesi | 🟡 Kısmi | Ayrı hesap yok (tek tenant içi modül, plana uygun); `OrgUnitId`/`LocationId` `IOrgUnitLookup`/`ILocationLookup` ile doğrulanıyor; `EmploymentType` yerel enum (Organization'da `IEmploymentTypeLookup` yok — R1) |
| 83 | ATS rol seti + ilan bazlı Erişim listesi | 🟡 Kısmi | `Recruiter`/`Interviewer` sistem rolleri zaten seed'li (`SystemRoleDefinitions.cs`, `recruitment.*` wildcard); `JobPostingAccess` (owned collection) + `SetJobPostingAccessCommand` var; "Admin − teklif şablonu" ayrı rolü ve ilan bazlı erişim UI'ı yok |
| 84–96 | Hızlı takvim, form builder, requisition, kural kümeleri, otomatik etiketleme, ilan durumları/yayın/URL (R3), başvuru formu 3 katman, e-posta şablonu, kariyer sitesi, ATS raporları | ❌ Başlanmadı | R1–R5'e bırakıldı (yukarıdaki paket tablosu) |
| — | (Roadmap'te madde yok) İlan CRUD + yaşam döngüsü (Draft/Open/OnHold/Closed/Archived), aday elle ekleme, başvuru + sabit pipeline (Applied/Screening/Interview/Offer/Hired/Rejected) | ✅ Bitti | Bu R0'ın çekirdeği — roadmap'te ayrı madde yok ama #84–96'nın önkoşulu |

## Mimari

- Modül: `Sense.Modules.Recruitment` (5 proje: Domain/Contracts/Application/Infrastructure/Api), Workflow modülünün şablonu birebir izlendi.
- Şema: `recruitment`. Entity'ler: `JobPosting` (+ owned `JobPostingAccess`), `Candidate`, `JobApplication`.
- İzinler: `recruitment.postings.{read,create,update,archive}`, `recruitment.candidates.{read,manage}`, `recruitment.applications.{read,manage}` — hepsi mevcut `recruitment.*` wildcard'ı (HR/HRSpecialist/Recruiter) tarafından kapsanıyor, `SystemRoleDefinitions.cs`'te değişiklik gerekmedi.
- Entegrasyon olayları: `JobPostingPublished`, `JobApplicationStageChanged` — outbox'a yazılıyor, `Sense.Worker` dinliyor; henüz tüketen yok (Notifications R1+'ta bağlanabilir).
- Web: `/app/recruitment/postings` (liste + oluştur + yayınla/kapat/arşivle), `/app/recruitment/postings/:id` (detay + başvuru ekle/aşama taşı), `/app/recruitment/candidates` (liste + ekle/düzenle); "Uygulamalar" başlatıcısında gerçek bağlantı (önceden `phase: 6` placeholder'dı).
- Doğrulama: `dotnet test` (Recruitment.Tests 10/10, mimari test paketi 38/39 — tek hata Employee modülünün ayrı, önceden var olan bir izin anahtarı sorunuyla ilgili, Recruitment'ı etkilemiyor), `tsc --noEmit`/`eslint`/`vite build` temiz. Arayüz tarayıcıda elle denenmedi.

## Açık noktalar / sonraki adım için notlar

- `Organization.Contracts.ApprovalRequestTypes` bugün `{Leave, Advance, Expense, Overtime}`; R1'de `JobRequisition` eklemek için Workflow ile contract-first bir PR gerekiyor.
- `IEmploymentTypeLookup` Organization.Contracts'ta yok; R0'da `RecruitmentEmploymentType` yerel enum olarak tutuldu (Employee'nin `EmploymentType`'ıyla aynı değerler).
- `Employee.Contracts`'ta hire→Employee köprüsü (`CreateFromCandidate`) hiç yok; R6/karar noktası.
- "Admin − teklif şablonu" ikinci Admin benzeri rolün adı ve Limited Member/Interviewer tam izin listeleri roadmap'te açık nokta olarak işaretli (11.3.11 §8); R1+'ta netleştirilecek.

## Değişiklik geçmişi

- 2026-09-16: R0 tamamlandı. Modül iskeleti + `JobPosting`/`Candidate`/`JobApplication` + hot-file kayıtları (`ModuleCatalog`, `Sense.Migrator`/`Sense.Worker`/`Sense.Api` Program.cs/csproj, `Sense.slnx`, `SharedResource{,.en}.resx`) + migration (`InitialCreate`) + web ekranları (postings/candidates/posting-detail) + i18n (`recruitment.json` tr/en/de). Doğrulama: backend `dotnet build`/`dotnet test` yeşil (Recruitment.Tests 10/10; mimari testler 38/39, tek hata Employee'nin önceden var olan `employee.profile.self.edit` izin anahtarıyla ilgili, bu pakete ait değil); web `tsc`/`eslint`/`vite build` temiz. Bu sırada aynı makinede eşzamanlı başka bir oturum Faz 2 (Attendance) ve Faz 3 (Payroll) üzerinde çalışıyordu — hot file'lara dokunuşlar dikkatle, o oturumun en güncel haline göre eklendi.
