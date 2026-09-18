# Tur 11 Sonuç

**Tarih:** 2026-09-18
**Kapsam:** Bu turda çalışma ağacında biriken tüm değişiklikler (148 dosya, +8533/−4671 - Reporting/Analytics
genişlemesi, Announcements/Support modülü, Attendance çalışma takvimi ataması, Leave/Employee rapor sağlayıcıları,
mobil Support ekranı ve kapanış turunda bulunup düzeltilen hatalar) ve tur kapanışında yapılan doğrulama.

**Nasıl okunur:** Faz bazlı ilerleme her fazın kendi `fazN-durum-takibi.md` dosyasındadır; bu doküman turun **doğrulama sonucunu**, kapanışta kapatılan hataları ve tur 12'ye devreden açık noktaları kaydeder. Aşağıdaki "kapatılan hatalar" bölümü yalnızca kapanış oturumunda bizzat doğrulanan işleri içerir — fazların kendi teslimatları ilgili faz dokümanlarında. Analytics/Reporting tarafının kendi PO değerlendirmesi ve karar kaydı:
[analitik-raporlama-po-degerlendirmesi-2026-09-18.md](../analitik-raporlama-po-degerlendirmesi-2026-09-18.md); ilgili faz durumu: [faz7-durum-takibi.md](faz7-durum-takibi.md), [faz4-raporlar-revizyonu.md](../../implementation/faz4-raporlar-revizyonu.md).

## 1. Doğrulama durumu

| Kontrol | Sonuç | Not |
|---|---|---|
| Web `tsc --noEmit` | ✅ Temiz | — |
| Web `eslint src` | ✅ 0 hata | 1 uyarı: `employees/employee-list.tsx:166` useMemo bağımlılığı |
| Web `vitest run` | ✅ 365/365 | §3.1'deki 31 Almanca anahtar kapanışın devamında çevrildi, bkz. §2.5 |
| Web `vite build` | ✅ Temiz | Prod build de doğrulandı |
| Backend `dotnet build Sense.slnx` | ✅ Temiz | Tüm modüller (Announcements dahil) hatasız derleniyor |
| Backend hedefli test paketleri | ✅ Yeşil | Kernel 31/31, Shared.Infrastructure 34/34, Analytics 23/23, Identity (UnitManagerGrants/SystemRoleDefinitions) 15/15, Architecture 52/52 |
| Backend tam çözüm `dotnet test Sense.slnx` | ⚠️ Tamamlanmadı | Tek koşuda uçtan uca raporlanamadı (koşu erken sonlandı, bir koşuda da `Sense.Tests.Shared` testhost paketleme hatasıyla "Test Run Aborted"). Modül bazlı koşular kullanılmalı; tur 12'de CI filtresiyle netleştirilmeli |
| Recruitment paketi | 🟡 70/71 | Tek kırık: §3.2 (testin kendi kurulum hatası) |
| Docker tam yığın | ✅ Ayakta | api + web + worker + postgres/redis/minio/mailpit, hepsi healthy |

Docker doğrulaması `docker compose up -d --build` ile yapıldı; `.env` portları ile web `:3000`, API `:8080` (`/health` 200), Mailpit `:8025`.

## 2. Kapanışta kapatılan hatalar

### 2.1 Recruitment: camelCase enum değerleri reddediliyordu (22 çağrı)

`POST /v1/recruitment/requisitions` `employmentType: "fullTime"` ve `reason: "newPosition"` için 400 `general.validation_error` döndürüyordu. Modül enum alanlarını `string` olarak alıp FluentValidation `IsEnumName(...)` ile doğruluyor; bu kural varsayılan olarak **case-sensitive**, enum üyeleri ise PascalCase (`FullTime`, `NewPosition`). Web ise projenin kendi API sözleşmesi gereği camelCase gönderiyor (`JsonStringEnumConverter(CamelCase)`, bkz. CLAUDE.md) — yani frontend doğruydu, backend kendi sözleşmesini reddediyordu.

Hata tek uçla sınırlı değildi; modüldeki 22 çağrının tamamı düzeltildi:

| Yer | Adet | Düzeltme |
|---|---|---|
| `IsEnumName(typeof(X))` | 11 | `caseSensitive: false` |
| `Enum.Parse<X>(...)` | 7 | `ignoreCase: true` |
| `Enum.TryParse<X>(...)` | 4 | `ignoreCase: true` |

Etkilenen uçlar: iş talepleri, ilan oluşturma/güncelleme/arşivleme, aday aşama taşıma, otomasyon kuralları, form şablonları ve `?status=` liste filtreleri. `FormTemplateCommands.cs` camelCase girdide `ArgumentException` fırlattığı için orada 400 değil **500** dönüyordu; o da kapandı.

Doğrulama (canlı API): `fullTime`/`newPosition` → **200**; `FullTime`/`NewPosition` → 200 (geriye dönük uyum korundu); `banana` → 400, `general.validation_error` sözleşmesi bozulmadan. Doğrulama gevşetilmedi, yalnız büyük/küçük harf duyarlılığı kaldırıldı.

> Not: Diğer modüller komut DTO'larında gerçek enum tipi kullanıyor (ör. `RecordAbsenceCommand(... AbsenceKind Kind ...)`), bu yüzden `IsEnumName` yalnız Recruitment'ta var. DTO'ları enum tipine çevirmek mimari olarak daha temiz olurdu; geçersiz değerde hata yanıtının şekli `general.validation_error`'dan model-binding hatasına döneceği ve frontend'in hata gösterimini etkileyeceği için bu turda tercih edilmedi. Tur 12 için değerlendirilebilir.

### 2.2 İki adımlı doğrulama ekranı sayfa yenilemede kayboluyordu

`login.tsx` içinde `mfaToken`/`mfaCode` yalnız component içi `useState`'te tutuluyordu — route değil, store değil, kalıcı depolama değil. Kod ekranındayken F5 (veya SPA state'ini sıfırlayan herhangi bir olay) kullanıcıyı **hiçbir uyarı vermeden** boş e-posta/şifre formuna düşürüyor, baştan giriş yapmayı gerektiriyordu. Backend'in `mfaToken`'ı 5 dakika geçerli ve stateless olduğu için sunucu tarafında sorun yoktu; kayıp tamamen istemci tarafındaydı.

Düzeltme: `mfaToken` `sessionStorage`'a (`senseik.mfaToken`) yazılıyor ve ilk yüklemede geri okunuyor; başarılı doğrulamada ve "Back to sign in" ile dönüşte siliniyor. Depolama erişilemezse (gizli sekme, engellenmiş çerez) akış eskisi gibi çalışmaya devam eder, yalnız yenilemeye dayanmaz.

Doğrulama: tarayıcıda login → 2FA ekranı → sayfa yenileme; ekran artık yerinde kalıyor.

### 2.3 Web imajını kıran 5 TypeScript hatası

`docker compose build web` başarısızdı; `pnpm --filter web build` içindeki `tsc` şu hatalarda duruyordu:

- `employees/assets-section.tsx:179` — `DatePicker`'a var olmayan `label` prop'u geçiriliyordu (etiket zaten üstteki `Text` ile veriliyor)
- `reports/weekly-overtime-cost-card.tsx:1` — kullanılmayan `ReactNode` importu
- `hooks/use-nav-visibility.test.ts` — test verisinde `"feature1"/"feature2"` artık geçerli `AccessSettingKey` değil (3 yer; gerçek anahtarlarla değiştirildi)
- `services/leave.service.test.ts` — kullanılmayan tip importları + `LeaveRequestOnBehalfInput`'ta zorunlu `bypassApproval` eksik

### 2.4 Commit'i bloklayan ESLint hataları

`lint-staged` staged web dosyalarında `eslint --fix` çalıştırıyor; `@typescript-eslint/no-explicit-any` otomatik düzeltilemediği için 8 hata pre-commit hook'unu düşürüyordu. `lib/api-error.test.ts` ve `services/password-reset.service.test.ts` tipleri korunarak düzeltildi (`unknown` + erişim noktasında dar cast); `services/leave.service.test.ts` eşzamanlı çalışan başka bir taraf tarafından indexed-access tiplerle düzeltildi.

### 2.5 Almanca çeviride 31 eksik anahtar (§3.1'de bulunan) çevrildi

`de/employees.json` (21), `de/leave.json` (8), `de/documents.json` (2) — tamamı `[DE] <İngilizce metin>` yer tutucusuydu, gerçek Almanca metinle değiştirildi (çalışma takvimi ataması, maaş "oluşturan/oluşturulma tarihi", izin talepleri filtreleri, çakışan izinler, belge arama). `pnpm vitest run`: 365/365 yeşil.

### 2.6 Kırık BE testi düzeltildi: `JobApplicationStageChangedHandler_HiredStage_TriggersProvisioning`

§3.2'de teşhis edilen tek satırlık hata giderildi: event artık ayrı bir `Guid.NewGuid()` yerine fake repoya kaydedilen `application.Id`'yi taşıyor; ilgili assertion da aynı değere çekildi.

## 3. Tur 12'ye devreden açık noktalar

### 3.1 Almanca çeviride 31 anahtar eksikti — ✅ kapanışta çözüldü

Tur kapanışında `vitest` 365/365 yeşile döndü (bkz. §2.5); aşağıdaki tablo hatanın bulunduğu andaki durumu kaydeder. `i18n.locales.test.ts` `[XX]` yer tutucusu tarıyordu ve 3 dosyada takılıyordu:

| Dosya | Eksik anahtar | Örnek |
|---|---|---|
| `de/employees.json` | 21 | `career.tabs.position`, `career.workSchedule.title` |
| `de/leave.json` | 8 | `requests.submittedAt`, `requests.filterType` |
| `de/documents.json` | 2 | `requirements.searchPlaceholder`, `requirements.searchEmpty` |

`tr` ve `en` temiz. Bu turda eklenen yeni ekranların anahtarları Almancaya geçmemiş.

> **Doküman tutarsızlığı:** `faz0-durum-takibi.md` kalem #13 hâlâ "`[DE]` yer tutucusu kalmadı" diyor. İfade yazıldığı tarihte doğruydu; bu turda eklenen anahtarlarla geçersizleşti. 31 anahtar çevrildiğinde ya kalem kanıtı güncellenmeli ya da "yeni anahtar eklendiğinde de/*.json da güncellenir" kuralı kaleme eklenmelidir.

### 3.2 Kırık BE testi: `JobApplicationStageChangedHandler_HiredStage_TriggersProvisioning` — ✅ kapanışta çözüldü (bkz. §2.6)

Üretim kodu doğru; **testin kendi kurulumu hatalı**. Test, application'ı fake repo'ya `application.Id` altında kaydediyor ama event'e ayrı bir `Guid.NewGuid()` (`applicationId`) koyuyor. Handler `@event.JobApplicationId` ile arayıp bulamıyor, "not found" uyarısıyla erken çıkıyor, dolayısıyla `ProvisionedCandidates.Count` 0 kalıyor. Bu test yazıldığı haliyle hiçbir zaman geçemez.

Düzeltme tek satır: event'e `application.Id` verilmeli. Recruitment paketinin kalanı yeşil (70/71). `tur(10)` mesajı "BE tamamen yeşil" dediğine göre bu regresyon tur 10 sonrası girmiş.

### 3.3 Demo ortamı artıkları

- `owner@demo.local` hesabında MFA açıktı ve kaydın yapıldığı authenticator kimsede olmadığı için hesap geçilemez bir kod ekranının arkasında kilitliydi. Ürünün kendi yönetici ucuyla sıfırlandı (`POST /v1/identity/users/{id}/mfa/reset`, `ik@demo.local` ile, 204). Artık hiçbir demo hesabında MFA açık değil.
- Recruitment doğrulaması sırasında demo veritabanında **"Test Talep"** ve **"Test2"** adlı iki iş talebi oluştu; temizlenmesi gerekir.

## 4. Ortam notları (yeni katılanlar için)

- **Portlar `.env` ile eziliyor:** depo dokümanı "standart port + 10000" kalıbını anlatıyor, ancak bu makinedeki `.env` API'yi `8080`, web'i `3000`, Mailpit'i `8025` yapıyor. Doküman kalıbına göre URL kurmayın, `.env`'e bakın.
- **2FA e-posta/SMS ile kod göndermez.** TOTP tabanlıdır (`BuildOtpAuthUri`); kod, kayıt sırasında bir kez gösterilen QR/secret'ı ekleyen authenticator uygulamasından okunur. "Doğrulama kodu gelmiyor" şikâyeti bu yüzden beklenen davranıştır.
- **Demo mailleri Mailpit'e düşer** (`Email__Smtp__Host=mailpit`), dışarı çıkmaz: <http://localhost:8025>. Mailpit mesajları bellekte tutar; container yeniden başlarsa veya kutu temizlenirse geçmiş kaybolur.
