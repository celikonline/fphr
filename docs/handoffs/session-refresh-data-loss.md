# senseik-053 — Oturum Yenileme Sırasında Veri Kaybı — UYGULAMA NOTU

**Durum: Uygulandı, `a/session-refresh-data-loss` branch'inde, yerel commit alındı (push YAPILMADI).**

## Kök Neden

Kullanıcıların ~15-30 dakikada bir sessizce login ekranına atılması bir "yarış durumu (race)" değil,
**hiç token yenileme mantığının olmamasıydı**:

- `src/Sense.Api/appsettings.json`: `AccessTokenMinutes: 15`, `RefreshTokenDays: 30`. Backend'de
  `POST /v1/identity/auth/refresh` (`RefreshTokenCommand` / `RefreshTokenHandler`,
  `AuthController.cs`) zaten var ve tam çalışıyor — rotasyon, reuse-detection, aile iptali dahil.
- Ama web tarafında (`packages/api-client/src/index.ts`) response interceptor 401 aldığı anda
  **hiçbir refresh denemesi yapmadan** direkt `clearSession()` + `window.location.assign("/login")`
  çağırıyordu. `web/src/store/auth.store.ts`'de `refreshToken` state'te tutuluyordu ama onu
  kullanan tek bir satır bile yoktu — tamamen ölü veri.
- Sonuç: 15 dakikalık access token süresi dolar dolmaz, kullanıcının o an tetiklediği İLK API
  çağrısı anında ve uyarısız oturumu bitiriyordu. Formda yazılmış her şey (izin talebi, çalışan
  ekleme formu, vb.) sessizce kayboluyordu.
- **İkincil/kalıcı sınırlama (bilerek dokunulmadı):** `refreshToken` zustand `persist`
  `partialize`'ında bilinçli olarak localStorage'a yazılmıyor (XSS'te iki token kopyası
  bulunmasın diye — koddaki mevcut yorum: "Full mitigation is httpOnly cookies... a backend
  change"). Bu, artık gerçek bir refresh akışı olsa bile, sayfa yenilendikten (F5) sonra
  bellekteki `refreshToken` kaybolduğu için access token süresi dolduğunda yine oturumun
  sonlanacağı anlamına geliyor. Bu kart bunu bilerek DEĞİŞTİRMEDİ (mevcut güvenlik kararına
  saygı duyuldu); kalıcı çözüm için httpOnly cookie + backend refresh-rotation gerekiyor —
  ayrı bir karar/kart olmalı.

## Yapılan Değişiklikler

### 1) Refresh-on-401 akışı (asıl düzeltme)

- **`packages/api-client/src/index.ts`**: `handleResponseError` adıyla dışa aktarılan (test
  edilebilir olsun diye) yeni bir response-error handler. 401 + `Authorization` header'ı olan
  (= oturum açmış) bir istek geldiğinde:
  1. Kayıtlı bir `RefreshHandler` varsa (`setRefreshHandler`) tek seferlik (single-flight, eşzamanlı
     birden çok 401 için tek network çağrısı — `refreshOnce()`) bir yenileme dener.
  2. Başarılıysa orijinal isteği `apiClient.request({...config, _retriedAfterRefresh: true})` ile
     TEKRAR gönderir (request interceptor yeni token'ı otomatik ekliyor, elle header taşımaya
     gerek yok).
  3. Başarısızsa veya handler yoksa `handleSessionExpired()`: `clearSession()` + kayıtlı
     `SessionExpiredHandler` (uyarı için) + `window.location.assign("/login")`. Bir
     `handlingSessionExpiry` bayrağıyla birden çok eşzamanlı 401 aynı anda tek bir
     temizlik/redirect üretir (birden fazla toast/redirect yarışı olmaz).
  4. `skipAuthRetry` (refresh çağrısının kendisi işaretler) ve `_retriedAfterRefresh` (yeniden
     denenmiş isteği işaretler) bayraklarıyla sonsuz döngü engellendi — bunlar `axios` modül
     augmentation'ı ile `AxiosRequestConfig`'e eklendi.
- **`web/src/store/auth.store.ts`**: yeni `refresh()` action'ı — bellekteki `refreshToken` yoksa
  hemen `false` döner (network çağrısı yapmaz); varsa `POST /v1/identity/auth/refresh`'i
  `{ skipAuthRetry: true }` ile çağırır, başarılıysa yeni access+refresh token'ı state'e ve
  `AUTH_TOKEN_STORAGE_KEY`'e yazıp `true` döner; her türlü hatada (asla throw etmeden) `false`
  döner — session'ı sonlandırma kararı kasıtlı olarak `api-client`'a bırakılıyor.
- **`web/src/lib/http-interceptors.ts`**: yeni `registerSessionRefreshHandlers()` — api-client'ın
  `setRefreshHandler`/`setSessionExpiredHandler`'ını auth store'a ve bir Mantine toast'a bağlıyor.
  Bu dosya zaten "web'e özgü interceptor kompozisyonu" için var olan yer (429 toast'ı burada),
  aynı deseni izliyor.
- **`web/src/main.tsx`**: `registerSessionRefreshHandlers()` çağrısı `registerHttpInterceptors()`
  ile birlikte eklendi.
- **i18n**: `common:errors.sessionExpiredTitle` / `sessionExpired` anahtarları `en`/`tr`/`de`
  `common.json`'a eklendi (TR: "Oturumunuz sona erdi, lütfen tekrar giriş yapın.").

### 2) Genel taslak (draft) kurtarma hook'u

`leave-request-dialog.tsx`'e **dokunulmadı** (talimat gereği — başka bir ajan tarafından
değiştiriliyor olabilir). Bunun yerine tamamen form-agnostik, tek başına test edilebilir bir hook
yazıldı ve **Employee modülünde** (PC-A'nın kendi hattı; Leave modülüne dokunmamak için de iyi bir
seçim) `create-employee-dialog.tsx`'e bağlandı:

- **`web/src/hooks/use-form-draft.ts`** (yeni): `useFormDraft<T>({ storageKey, value, enabled,
  onRestore, isEmpty?, debounceMs? })`. `enabled` (örn. dialog'un `open` state'i) `false→true`
  geçtiğinde localStorage'daki taslağı bir kez geri yükler; `enabled` `true` iken `value`
  değiştikçe debounce'lu (varsayılan 800ms) olarak localStorage'a yazar; `clearDraft()` başarılı
  submit sonrası çağrılmak üzere döner. `localStorage` erişimi private-mode/devre dışı senaryolar
  için try/catch ile korunuyor (`auth.store.ts`'deki `safeStorageSet` deseniyle aynı).
  `web/src/hooks/index.ts`'e eklendi.
- **`web/src/components/employees/create-employee-dialog.tsx`**: `values` + `orgUnitId` + `manager`
  birleşik bir `CreateEmployeeDraft` olarak `senseik.draft.employee-create` anahtarıyla
  kaydediliyor/geri yükleniyor; `isEmpty` tüm alanlar boşsa taslağı yazmıyor/temizliyor; mutation
  `onSuccess`'te `clearDraft()` çağrılıyor (Cancel/kapatmada TASLAK BİLEREK SİLİNMİYOR — kullanıcı
  yanlışlıkla kapatırsa bir dahaki açılışta geri gelsin diye).

## Testler (hepsi yeşil — Docker `node:24-alpine` + pnpm ile)

- `web/src/lib/api-client-refresh.test.ts` (yeni, 6 test): gerçek `handleResponseError`'ı
  (mocklanmamış `@senseik/api-client`, sadece `axios.create` sahte bir instance'a yönlendirilmiş)
  doğrudan çağırıyor —anonim 401'in dokunulmadan geçtiğini, başarılı refresh sonrası tek seferlik
  retry'ı, refresh **başarısız olduğunda** session'ın temizlenip `sessionExpiredHandler`'ın **tam
  bir kez** çağrıldığını, eşzamanlı 401'lerin tek bir refresh çağrısına indirgendiğini (dedupe),
  ve `_retriedAfterRefresh`/`skipAuthRetry` bayraklarının sonsuz döngüyü engellediğini kanıtlıyor.
- `web/src/store/auth.store.test.ts` (yeni, 3 test): `refresh()` — refreshToken yokken network
  çağrısı yapmadan `false`; başarılı rotasyonda state+localStorage güncelleniyor ve
  `skipAuthRetry: true` gönderiliyor; backend reddettiğinde throw etmeden `false` dönüyor.
- `web/src/lib/http-interceptors.test.ts` (genişletildi, +2 test): `registerSessionRefreshHandlers`
  auth store'un `refresh()`'ini doğru bağlıyor ve session-expired durumunda **"Oturumunuz sona
  erdi, lütfen tekrar giriş yapın"** toast'ının gösterildiğini doğruluyor (görevin istediği "uyarı
  gösteriliyor" senaryosu).
- `web/src/hooks/use-form-draft.test.ts` (yeni, 6 test): debounce'lu kayıt, disabled iken
  kayıt/geri-yükleme yapılmaması, `enabled` açıldığında taslağın geri yüklenmesi, `clearDraft()`,
  boş değerde taslağın silinmesi, bozuk (JSON.parse patlayan) taslağın throw etmeden atılması.

Doğrulama (Docker `node:24-alpine`, host'ta pnpm/node yok):
`pnpm exec tsc --noEmit` → hatasız. `pnpm exec eslint src` → 0 hata (1 önceden var olan, bu
değişiklikle ilgisiz `react-hooks/exhaustive-deps` uyarısı `employee-list.tsx`'te duruyor).
`pnpm exec vitest run` → **399/400 geçti**; tek hata `src/i18n.locales.test.ts` →
`attendance.json` `de` çevirisinde önceden var olan çevrilmemiş `teamTimesheet.week/month`
anahtarları — bu dala hiç dokunulmadı, senseik-053 ile ilgisi yok. `pnpm exec vite build` →
başarılı (production build, PWA precache dahil).

## Değişen/Eklenen Dosyalar

- `packages/api-client/src/index.ts`
- `web/src/store/auth.store.ts`, `web/src/store/auth.store.test.ts` (yeni)
- `web/src/lib/http-interceptors.ts`, `web/src/lib/http-interceptors.test.ts`
- `web/src/lib/api-client-refresh.test.ts` (yeni)
- `web/src/hooks/use-form-draft.ts` (yeni), `web/src/hooks/use-form-draft.test.ts` (yeni),
  `web/src/hooks/index.ts`
- `web/src/components/employees/create-employee-dialog.tsx`
- `web/src/main.tsx`
- `web/public/locales/{en,tr,de}/common.json`

## Kritik Dosyalar / Bilinmesi Gerekenler

- `src/Modules/Identity/Sense.Modules.Identity.Application/Auth/RefreshAndLogout.cs`,
  `src/Modules/Identity/Sense.Modules.Identity.Api/Controllers/AuthController.cs` — backend
  refresh/logout uçları (dokunulmadı, sadece incelendi).
- `src/Sense.Api/appsettings.json` — `AccessTokenMinutes`/`RefreshTokenDays` (dokunulmadı).
- `web/src/components/leave/leave-request-dialog.tsx` — **kasıtlı olarak dokunulmadı** (başka bir
  ajanın hattında olabilir). `useFormDraft` hook'u form-agnostik yazıldığı için, o dosyaya
  dokunmadan sorumlu olan ajan/kişi tek satırlık bir entegrasyonla (bkz.
  `create-employee-dialog.tsx`'teki örnek) aynı korumayı ekleyebilir.
- Kalıcı sınırlama (bkz. "Kök Neden"): sayfa yenilemesi + bellekteki `refreshToken`'ın
  kaybolması — httpOnly cookie tabanlı refresh olmadan tam çözülmüyor. Ayrı bir karar/kart
  önerilir.

---
*Kaynak: `docs/agent-board/cards/senseik-053-session-refresh-data-loss.json` (bu worktree'de kart
dosyası bulunamadı — muhtemelen başka bir paralel oturumda oluşturuldu; görev talimatı doğrudan
uygulandı).*
