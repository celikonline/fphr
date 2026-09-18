# senseik-008 — i18n Enum Anahtarı Sızıntıları (B-02, H-03) — UYGULAMA PLANI

## Implementasyon Notu

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `b/i18n-enum-leaks`.**

- **Bug 1** (`leave-type-dialog.tsx`): `useLeaveName()` eklendi. `settings.name` state'i ham değeri taşımaya devam
  ediyor; `TextInput`'un görünen değeri `leaveName(settings.name)`'den türetilen `displayName` — kullanıcı yazınca
  `set("name", ...)` ham state'i doğrudan override ediyor (artık anahtar değil, serbest metin). Kaydet, state'i değil
  `displayName`'i gönderiyor (`{ ...settings, name: displayName }`) — hem düzenlenmemiş (ham anahtar → çözülmüş
  metin) hem düzenlenmiş (kullanıcı metni, `leaveName` no-op çünkü `"leave."` ile başlamıyor) durumda ham anahtar asla
  API'ye gitmiyor. **Ürün kararı notu plan'da işaretlendiği gibi hâlâ geçerli**: sistem tohumlu bir türü hiç
  dokunmadan kaydetmek artık onu "özel" bir isme çeviriyor (bir daha `leave:names.*`'ten çözülmeyecek) — dil
  değiştirildiğinde eski dildeki metin sabitlenmiş kalır. QA/PO onayı gerekebilir; şimdilik plan'ın istediği davranış
  budur.
- **Bug 2** (`web/src/lib/i18n-enum.ts`, yeni): `enumLabel(t, key, raw)` — ham değer → `lowerFirst` varyantı →
  `humanize(raw)` sırasıyla dener, hiçbir dalda `ns:path.X` formatını döndürmez. `candidates.tsx`'e uygulandı.
- **Yüksek risk listesi** (aynı kusur ailesi) tamamı `enumLabel`'a geçirildi: `job-postings.tsx`,
  `job-requisitions.tsx`, `job-posting-detail.tsx`, `recruitment-automation-rules.tsx`,
  `recruitment-form-templates.tsx`, `public/career/job-posting.tsx`, `public/career/postings.tsx`. Sadece API'den gelen
  enum alanları (`.status`, `.employmentType`, `.templateType`, `.stage`, `.fromStage`/`.toStage`) değiştirildi;
  frontend'in kendi sabit dizilerini (`RECRUITMENT_EMPLOYMENT_TYPES`, `APPLICATION_STAGES` vb.) `Select` seçenekleri
  için eşleyen döngüler dokunulmadan bırakıldı (risk yok, backend serileştirmesinden bağımsız).
- **Ad hoc yamalar konsolide edildi**: `payments.tsx` (`row.status.toLowerCase()` + `defaultValue`) ve
  `users-roles.tsx` (`accessType.toLowerCase()`, iki yer) artık `enumLabel` kullanıyor.
- **Backend companion** (kapsam dahilinde, küçük): `CandidateDtos.cs`'te `CandidateDto.Source` artık `string` değil
  gerçek `CandidateSource` enum'u; `CandidateQueries.cs`'teki `.ToString()` kaldırıldı. Global
  `JsonStringEnumConverter(CamelCase)` artık devreye giriyor, API `"manual"` döndürüyor (`"Manual"` değil).
  **Aynı aile 15+ dosya (`JobPostingQueries.cs`, `JobRequisitionQueries.cs`, `JobApplicationQueries.cs`,
  `AutomationRuleQueries.cs`, `FormTemplateQueries.cs`, `PublicCareerQueries.cs`, ve
  Announcements/Documents/Employee/Expense/Identity/Training/Workflow modüllerinde) hâlâ aynı `.ToString()` deseniyle
  PascalCase sızdırıyor — bu kart kapsamında dokunulmadı.** Frontend tarafı `enumLabel` sayesinde bu dosyalar
  düzeltilmeden de kendini koruyor (lowerFirst fallback), ama kalıcı çözüm backend companion'ların da aynı şekilde
  düzeltilmesi.

**Testler (hepsi yeşil, host Docker üzerinden doğrulandı):**
- `web/src/lib/i18n-enum.test.ts` — 6 test (üç durum + regex ile `ns:path.X` asla dönmediği doğrulaması).
- `web/src/components/leave/leave-type-dialog.test.tsx` — Bug 1 regresyonu (RTL): ham anahtar görünmüyor, düzenlenmemiş
  kayıtta çözülmüş metin gönderiliyor, düzenlenmiş kayıtta kullanıcı metni gönderiliyor.
- `web/src/pages/recruitment/candidates.source-label.test.ts` — Bug 2 regresyonu: PascalCase simülasyonu (backend
  düzeltilmemiş gibi) ve camelCase (backend düzeltilmiş gibi) her ikisinde de doğru etiket, hem tr hem en paketiyle.
- `tests/Modules/Sense.Modules.Recruitment.Tests/CandidateDtoSerializationTests.cs` — backend entegrasyon: API'nin
  gerçek `JsonSerializerOptions` şekliyle `CandidateDto` serileştirilince `source` camelCase dönüyor, `"Source"`
  (PascalCase alan adı) hiçbir yerde geçmiyor.
- Doğrulama: `pnpm exec tsc --noEmit` temiz, `pnpm exec eslint src` temiz (1 ilgisiz pre-existing uyarı,
  `employee-list.tsx`), `pnpm exec vite build` başarılı, `pnpm exec vitest run` 377/378 yeşil (1 ilgisiz pre-existing
  hata: `de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder, bu kartın dokunmadığı dosya).
  Backend: `dotnet build`/`dotnet test` (Recruitment modülü) 74/75 yeşil — 1 ilgisiz pre-existing hata
  (`JobApplicationStageChangedHandler_HiredStage_TriggersProvisioning`, testin kendi kurgusunda `candidateId` ile
  `Candidate.Create`'in ürettiği gerçek `candidate.Id` eşleşmiyor; kod içi yorum bunun "hiçbir zaman geçmediğini"
  zaten belgeliyor — bu kartın kapsamı dışı, dokunulmadı).

**Backlog önerisi — "geniş rollout" kartı:** Bu kart yalnızca Bug 1 + Bug 2 + yüksek risk listesini kapsıyor;
230 çağrı noktasının geri kalanı (ve yukarıdaki 15+ dosyalık backend `.ToString()` ailesi) taranıp `enumLabel`'a
geçirilmedi. Önerilen yeni kart: (1) `rg "t\\(\`[a-z]+:.*\\$\\{"` ile web genelinde kalan çağrı noktalarını tara,
her birinin API'den mi yoksa frontend sabit diziden mi geldiğini ayırt et; (2) sadece API-kaynaklı olanları
`enumLabel`'a geçir; (3) paralelde backend companion'ları (`JobPostingQueries.cs` vb.) DTO alanlarını enum tipinde
bırakacak şekilde düzelt — ikisi birlikte yapılırsa `enumLabel`'ın fallback yolu artık hiç tetiklenmemeli, bu da
regresyonla test edilebilir bir hedef. Tahmini kapsam: 15+ backend dosyası, ~200 frontend çağrı noktası —
tek kart yerine modül bazlı (Recruitment kalanı / Payroll / Identity / Workflow vb.) alt kartlara bölünmesi önerilir.

---

## Kök Neden — İKİ AYRI kök neden, tek belirti

**Bug 1 — İzin türü düzenleme formu (frontend init hatası, gerçek anahtar eksikliği YOK):**
`web/src/components/leave/leave-type-dialog.tsx:113,161-169` — `settings.name` seed veride ham kaynak anahtarı (`"leave.type.annual"`) olarak saklanıyor; bunu çözecek `useLeaveName()` hook'u zaten var ve `leave:names.type_annual` anahtarı `leave.json`'da mevcut. `LeaveTypeDialog` sadece bu hook'u çağırmayı atlamış. Kardeş bileşen `leave-rule-sets.tsx:286-290` (`RenameRuleSetDialog`) doğru pattern'i uyguluyor.

**Bug 2 — Adaylar listesi Kaynak sütunu (backend serialization hatası, frontend'de savunma YOK):**
`candidates.tsx:296,345`: `t(\`recruitment:candidates.sources.${candidate.source}\`)`. Anahtarlar (`manual/careerSite/referral/other`) camelCase ve doğru. Sorun backend'de: `CandidateQueries.cs:41` `candidate.Source.ToString()` enum'u PascalCase string'e (`"Manual"`) çeviriyor — global `JsonStringEnumConverter(CamelCase)` yalnız alan tipi enum kalırsa devreye girer, `.ToString()` sonrası düz string olduğu için converter tetiklenmez. Frontend `candidates.sources.Manual` anahtarını bulamayıp ham haliyle gösteriyor.

**Yapısal doğrulama:** Aynı `.ToString()`-into-string-DTO deseni backend'de 15+ dosyada tekrarlanıyor (`JobPostingQueries.cs`, `JobRequisitionQueries.cs`, `JobApplicationQueries.cs`, `AutomationRuleQueries.cs`, `FormTemplateQueries.cs`, `PublicCareerQueries.cs`, ve Announcements/Documents/Employee/Expense/Identity/Training/Workflow modüllerinde). `payments.tsx`/`users-roles.tsx` bu sınıf hatayı zaten `.toLowerCase()` ile ad hoc yamalamış — sistemleştirilmemiş.

**Sonuç: Kök neden "eksik namespace anahtarı" değil — backend enum→JSON serileştirme tutarsızlığı + frontend'in buna karşı savunmasının olmaması.**

## Etkilenen Dosyalar

**Kesin hata (bu kartta düzeltilecek):** `leave-type-dialog.tsx`, `candidates.tsx`

**Aynı kusur ailesi, yüksek risk (manuel taramaya dahil):** `job-postings.tsx`, `job-requisitions.tsx`, `job-posting-detail.tsx`, `recruitment-automation-rules.tsx`, `recruitment-form-templates.tsx`, `public/career/*`

**Zaten ad hoc yamalanmış, konsolide edilmeli:** `payments.tsx`, `users-roles.tsx`

**Backend asıl kaynak (ayrı companion ticket önerilir):** `CandidateQueries.cs` + `CandidateDtos.cs`, ve aynı aile (`JobPostingQueries.cs` vb.)

## Kalıcı Çözüm

**Bug 1:** Yeni abstraction yok (YAGNI) — `LeaveTypeDialog`'un `useState` initializer'ını `RenameRuleSetDialog`'daki mevcut pattern'e eşitle (`useLeaveName()` kullan). Not: kaydet sonrası davranış (localized text mi orijinal key mi API'ye gider) bir ürün kararı, QA/PO ile teyit edilmeli.

**Bug 2 + geniş risk grubu:** Küçük, gerekçelendirilmiş paylaşılan yardımcı `web/src/lib/i18n-enum.ts` — `enumLabel(t, key, raw)`: önce ham değeri, sonra `lowerFirst` (PascalCase→camelCase) varyantını dener, hiçbir zaman ham anahtar döndürmez (en kötü `humanize(raw)`). Sadece Bug 2 + yüksek-risk recruitment listesine uygula; 230 çağrı noktasının tamamını bu kartta migrate etme — kapsam dışı, backlog'a "geniş rollout" kartı öner.

**Backend companion ticket (şiddetle önerilir):** `.ToString()` yerine DTO alanını gerçek enum tipiyle bırak (global converter otomatik camelCase üretir) veya `JsonNamingPolicy.CamelCase.ConvertName(...)` kullan — olmadan frontend yardımcısı bir yama, kalıcı çözüm değil.

## Eksik Namespace Anahtarları

**Bu kartın iki somut hatası için EKSİK ANAHTAR YOK** — `leave:names.type_annual` ve `recruitment:candidates.sources.*` tam ve doğru mevcut (tr/en/de). Bu bir çeviri içeriği eksikliği değil, çözümleme mantığı hatası.

## Test Planı

1. `i18n-enum.test.ts` (yeni) — `enumLabel` üç durum: ham eşleşir, lowerFirst fallback eşleşir, hiçbiri eşleşmezse `humanize(raw)` (asla `ns:path.X` formatı dönmez).
2. Regresyon — Bug 1: `settings.name="leave.type.annual"` render → `"Yıllık izin"` gösterir, ham anahtar değil.
3. Regresyon — Bug 2: `source:"Manual"` (PascalCase simülasyonu) render → doğru etiket gösterir, ham anahtar değil (backend düzeltilse de düzeltilmese de frontend kendini korur).
4. Statik kontrol — mevcut `i18n.locales.test.ts`'e küçük kaynak-taraması eklentisi (custom ESLint kuralı YAZMA, orantısız).
5. Manuel tarama (merge_gate'te zaten var) — izin türü modalı, adaylar listesi, iş ilanları listesi.
6. Backend companion — `CandidateQueries` etrafında entegrasyon testi: API yanıtındaki `source` camelCase (`"manual"`), `"Manual"` değil.

## Kritik Dosyalar
- `web/src/components/leave/leave-type-dialog.tsx`
- `web/src/pages/recruitment/candidates.tsx`
- `web/src/lib/i18n-enum.ts` (yeni, önerilen)
- `web/src/pages/settings/leave-rule-sets.tsx` (referans pattern)
- `src/Modules/Recruitment/Sense.Modules.Recruitment.Application/Candidates/CandidateQueries.cs` (backend companion, ayrı ticket)

---
*Kaynak: 1. Tur B-02, 2. Tur H-03. Agent board: `docs/agent-board/cards/senseik-008-i18n-enum-anahtar.json`.*
