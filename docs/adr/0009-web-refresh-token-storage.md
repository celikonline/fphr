# ADR 0009 — Web istemcisinde refresh token saklama (F5 sonrası oturum sürekliliği)

**Durum:** Kabul edildi (ara çözüm) — 2026-09-20. Kalıcı çözüm: httpOnly cookie (aşağıda "Sonraki adım").

## Bağlam
Access token 15 dk (`IdentityDefaults.AccessTokenMinutes`) yaşar; refresh token API'de rotasyonlu (eski token iptal, yeniden kullanım = tüm aile iptali; `RefreshAndLogout.cs`). Web'de `web/src/store/auth.store.ts` refresh token'ı yalnızca bellekte tutuyordu (zustand `partialize` yalnızca `user`/`features`; access token `localStorage["auth_token"]`). Tam sayfa yenilemeden (F5) sonra bellek boşaldığı için access token dolunca `refresh()` refresh token bulamayıp `false` dönüyor, `@senseik/api-client` kullanıcıyı `/login`'e atıyordu — formdaki girilmiş veri kayboluyordu (S-53'ün sessiz yenilemesi F5 sonrası işlemiyordu). Ayrıca login sayfasındaki "Beni hatırla" onay kutusunun durumu hiçbir yerde okunmuyordu (etkisiz, yanıltıcı).

## Seçenekler
1. **Refresh token'ı httpOnly + SameSite cookie'de tutmak** (tercih edilen uzun vade). Bu aşamada reddedildi: Identity API'nin `/refresh` ve `/logout` uçlarının cookie'den okuması/yazması, CORS `AllowCredentials` + kesin origin, CSRF savunması (refresh/logout cookie ile çalışacağı için `Origin`/özel başlık doğrulaması), mobilin gövdeden refresh token akışını koruyan çift mod ve dağıtım topolojisi gerekir. Compose'ta web (`:13000`) ile API (`:18080`) farklı origin (`VITE_API_BASE_URL` mutlak URL); prod'da alan adı/site topolojisi henüz sabit değil (aynı site değilse `SameSite=None; Secure` + üçüncü taraf çerez engelleri). Sözleşme (mobil + api-client) bozulmadan ayrı, test edilmiş bir iş olarak yapılmalı.
2. **`localStorage`'a yazmak.** Reddedildi: tarayıcı kapandıktan sonra da yaşayan, döndürülen (uzun ömürlü) bir kimlik bilgisi XSS ile sızdırılabilir.
3. **`sessionStorage`'a yazmak** — seçildi. F5'te korunur, sekme/tarayıcı kapanınca silinir.

## Karar
- Refresh token **yalnızca `sessionStorage["refresh_token"]`**'da tutulur (`REFRESH_TOKEN_STORAGE_KEY`, `@senseik/api-client`'ta dışa aktarılır). `localStorage`/zustand `persist` içine **asla** yazılmaz. Store oluşturulurken senkron okunur; böylece F5 sonrası `refresh()` token'ı bulur.
- Sayfa yüklenirken access token süresi dolmuş/yoksa ve refresh token varsa, `hasHydrated` **refresh denemesi bitene kadar** `false` kalır (`onRehydrateStorage`); `ProtectedRoute` yükleme ekranını gösterir, süresi dolmuş token yüzünden `/login`'e yönlendirmez. Refresh reddedilirse mevcut akış işler (oturum sona erdi bildirimi + `/login`).
- Login/MFA başarısında, `refresh()` başarısında ve `setToken`'da yazılır; login hatasında, `logout()`'ta ve api-client'ın oturum-sonu temizliğinde (`clearSession`) silinir.
- `logout()` ayrıca `POST /v1/identity/auth/logout` ile refresh token'ı sunucuda iptal etmeyi **en iyi çabayla** dener (access token süresi dolmuşsa atlanır, hata yutulur, `skipAuthRetry`); böylece kopyalanmış bir refresh token işe yaramaz.
- **"Beni hatırla" kaldırıldı.** `localStorage` kullanılamadığı için tarayıcı yeniden başlatmasını aşan güvenli bir kalıcı oturum bu ara çözümle mümkün değil; işlevsiz bir onay kutusu yanlış vaat verir. Oturum sekme ömrü kadardır. Kalıcı oturum httpOnly cookie ile geri getirilecek (`auth:rememberMe` çeviri anahtarları bu yüzden silinmedi).
- Mobil (`apps/senseik-mobile`) ve API sözleşmesi değişmedi: `/login`, `/refresh`, `/logout` gövde tabanlı kalır.

## Kabul edilen riskler
- **XSS:** Bu origin'de çalışan bir betik `sessionStorage`'ı access token ile aynı şekilde okuyabilir (httpOnly değil). Azaltma: nginx CSP (`script-src 'self'`), refresh rotasyonu + yeniden kullanım tespiti, `logout` sunucu iptali, `localStorage`'da uzun ömürlü kopya yok.
- **Çok sekme:** `sessionStorage` sekme başınadır. Yeni sekmede (`Ctrl+tık` vb.) refresh token yoktur; paylaşılan (localStorage) access token dolunca o sekme login ister. Sekmeyi *çoğaltmak* `sessionStorage`'ı kopyalar; bir sekme token'ı döndürdükten sonra diğerindeki kopya bayatlar ve kullanıldığında sunucu yeniden-kullanım tespitiyle tüm aileyi iptal eder (kullanıcı yeniden giriş yapar). Bu nadir durum kabul edildi; cookie çözümü ortadan kaldırır.
- Access token hâlâ `localStorage`'da (15 dk; tarayıcı kapanınca da kalır) — mevcut davranış, bu ADR kapsamı dışında.

## Sonraki adım
httpOnly + `Secure` + `SameSite` refresh cookie'si: Identity `/refresh` ve `/logout` cookie'den okur/yazar (mobil için gövde modu korunur), CORS `AllowCredentials` + izinli origin (`Frontend:BaseUrl`), CSRF için `Origin` doğrulaması, web'de token sakınımının kaldırılması ve "Beni hatırla"nın kalıcı/oturum çerezi olarak geri gelmesi. Web ve API aynı site altında (ör. `app.` / `api.` alt alanları veya ters vekil) yayınlanınca uygulanmalı.
