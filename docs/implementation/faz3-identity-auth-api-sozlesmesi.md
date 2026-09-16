# Faz 3 — Kimlik/Oturum/MFA/KVKK API sözleşmesi (hrteam Faz 3, Identity modülü)

Bu doküman hrteam'in "Faz 3" (kimlik, yetki, self-servis) çalışmasının backend tarafında `src/Modules/Identity/**`
altına eklenen/değiştirilen uçları belgeler. RBAC (16 sistem rolü, joker izin desenleri, `SystemRolePermissionSynchronizer`,
alan bazlı görünürlük) **bu fazda değiştirilmedi** — olgun ve doğru çalıştığı için bilinçli olarak dokunulmadı; bkz.
`docs/analysis/kolayik-video/faz0-durum-takibi.md` / `faz1a-durum-takibi.md`. Bu doküman yalnız Faz 3 turlarında
**yeni eklenen** akışları kapsar: şifre sıfırlama, MFA (TOTP), yönetici MFA sıfırlama, davet kabulünde KVKK aydınlatma
metni sürümü, `ITenantSlugLookup` (Faz 9 köprüsü).

## Şifre sıfırlama

Davet akışının token kalıbını izler (hash'lenmiş token, expiry, tek kullanımlık, kullanıcı numaralandırmasına izin
vermez — var olmayan e-posta için de aynı 204 yanıtı).

### `POST /api/v{version}/identity/auth/forgot-password`
- Anonim, `[EnableRateLimiting("public-anonymous-write")]`.
- Gövde: `{ email: string }` → 204 (her zaman, e-posta bulunsa da bulunmasa da).

### `POST /api/v{version}/identity/auth/reset-password`
- Anonim, `[EnableRateLimiting("public-anonymous-write")]`.
- Gövde: `{ token: string, newPassword: string }` → 204.
- Hatalar: `password_reset.not_found`, `password_reset.expired`; parola politikası `IdentityOptions.MinPasswordLength`
  (bugün başka bir politika yok — karmaşıklık/geçmiş/periyodik değişim kapsam dışı).

## MFA (TOTP, RFC 6238)

TOTP: BCL `HMACSHA1` ile üretildi (yeni NuGet paketi yok — `Otp.NET` referansı `Directory.Packages.props`'ta durur
ama kullanılmaz, ölü referans, temizliği PO kararı). Secret `IEncryptionProvider` ile şifreli saklanır
(`User.TotpSecretEncrypted`). `mfaToken` (login tamamlama) durumsuzdur: `ITimeLimitedDataProtector`
(purpose `identity.mfa.login`, `IdentityOptions.MfaTokenMinutes` = 5 dk) ile imzalanır, kullanıcı+kiracı kimliğini
taşır — sunucuda tablo/satır tutmaz, tek kullanımlık **değildir** (süresi dolana kadar birden fazla `complete`
denemesi yapılabilir).

### `GET /api/v{version}/identity/me/mfa`
- Auth zorunlu (`[Authorize]`, controller seviyesinde).
- Yanıt: `MfaStatusDto { enabled: bool, pendingEnrollment: bool }`.
  `pendingEnrollment` = secret yazılmış (`enroll` çağrılmış) ama henüz doğrulanmamış (`TotpSecretEncrypted` dolu,
  `MfaEnabled` false).

### `POST /api/v{version}/identity/me/mfa/enroll`
- Auth zorunlu. Enrollment 1. adım: secret üretilir, şifreli yazılır, `MfaEnabled` **açılmaz**.
- Yanıt: `MfaEnrollmentDto { secret: string, otpAuthUri: string }`.
- 409 `identity.mfa.already_enabled`: MFA zaten açıksa.
- İkinci bir `enroll` çağrısı önceki bekleyen secret'ın üstüne yazar (kullanıcı QR'ı yeniden okutup baştan
  başlayabilir) — ayrı bir "pending" alanı/migration yok.

### `POST /api/v{version}/identity/me/mfa/verify`
- Auth zorunlu. Gövde: `{ code: string }` → 204; doğruysa `MfaEnabled=true` olur.
- 400 `identity.mfa.invalid_code`.

### `POST /api/v{version}/identity/me/mfa/disable`
- Auth zorunlu (self-servis kapatma, kod ile onay). Gövde: `{ code: string }` → 204.
- 400 `identity.mfa.invalid_code`.

### `POST /api/v{version}/identity/auth/login`
- Değişmedi (mevcut `LoginResultDto`), ama MFA açık kullanıcıda artık token **verilmeden**
  `{ status: "mfa_required", mfaToken: string, tokens: null }` döner (null alanlar `WhenWritingNull` ile omit edilir).

### `POST /api/v{version}/identity/auth/mfa/complete`
- Anonim, `[EnableRateLimiting("public-anonymous-write")]`. Login'in `mfaRequired` adımından sonraki adım.
- Gövde: `{ mfaToken: string, code: string }` → normal login yanıtı (`LoginResultDto`, `status: "ok"`).
- Hatalı kod `RecordFailedAccess`/lockout mekanizmasına dahildir (parola hatasıyla aynı sayaç).
- Hatalar: `identity.mfa.invalid_code` (401, parola hatasıyla tutarlı — enroll/verify/disable'daki 400'den farklı,
  bilinçli tasarım kararı), `identity.mfa.token_expired` (401).

### Yönetici MFA sıfırlama — `POST /api/v{version}/identity/users/{userId}/mfa/reset`
- Yetki: `identity.users.manage`.
- Secret temizlenir + `MfaEnabled=false` (kullanıcı bir sonraki girişte MFA'yı sıfırdan kurar) → 204.
- 409 `identity.mfa.self_reset_forbidden`: kendi hesabına bu uçtan uygulanamaz (self-servis `mfa/disable`
  kullanılmalı).
- 404 `user.not_found`: farklı kiracının kullanıcısı (tenant filtresi `IUserRepository.GetByIdAsync`'te otomatik).
- Not: bu uç ilk yazıldığında `identity.users.reset_mfa` ayrı izniyle geldi ama hiçbir sistem rolünün include
  listesinde değildi (kullanılamaz durumdaydı) ve rota `{id}/reset-mfa` idi; Faz 3 gün açılışı sözleşmesiyle
  hizalandı (`identity.users.manage`, rota `{id}/mfa/reset`, self-reset guard eklendi). `identity.users.reset_mfa`
  izin anahtarı kataloğda **yetim** kaldı (hiçbir uç/rol referans etmiyor) — temizlik PO kararına bırakıldı.

## Davet kabulü — KVKK aydınlatma metni sürümü

### `POST /api/v{version}/identity/invitations/accept`
- Anonim. Gövde: `AcceptInvitationRequest { token, password, privacyNoticeAccepted, privacyNoticeVersion }`.
- `privacyNoticeVersion`: `privacyNoticeAccepted=true` iken **zorunlu**, en fazla 20 karakter. Ürün genelinde tek
  sabit değer: `"2026-09"` (Faz 9 ile aynı; web `auth:privacy.noticeVersion` i18n anahtarından okunur).
- Kalıcı rıza kaydı (Faz 1'in `IConsentRecorder`/`ConsentRecord`, S-3) henüz bağlı **değil** — implementasyon
  gelince `AcceptInvitationHandler`e enjekte edilip `RecordAsync(...)` çağrılacak (bkz. handler'daki TODO yorumu).
  Bu turda değer yalnız (a) `ILogger` ile denetim amaçlı loglanır, (b) `InvitationAccepted` domain olayına
  (`Sense.Modules.Identity.Domain.Invitations`) opsiyonel `PrivacyNoticeVersion` alanı olarak eklendi (yalnız
  ekleme, tüketicisi yok, ileride bir abone eklenebilir).

## Cross-module contract: `ITenantSlugLookup` (ADR-0007)

`Sense.Modules.Identity.Contracts.ITenantSlugLookup`:
```csharp
public interface ITenantSlugLookup
{
    Task<Guid?> FindTenantIdAsync(string slug, CancellationToken cancellationToken);
}
```
- Implementasyon: `Identity.Infrastructure/Security/TenantSlugLookup.cs`, `ITenantRepository.GetBySlugAsync`
  sarmalayıcısı, `AddScoped`.
- Bulunamayan slug **veya** pasif kiracı (`Tenant.IsWritable` false — `Suspended`/`Closed`) → `null`, istisna yok.
- Tüketici: `Sense.Modules.Recruitment` (Faz 9), kariyer sayfası public ilan listesi
  (`ListPublicJobPostingsByTenantSlugQuery`) — `null` dönünce boş liste (404 değil, tenant slug'larının
  numaralandırılmasını önlemek için bilinçli tasarım).

## Kapsam dışı (bu fazda dokunulmadı, bilinçli)

- SSO/OAuth/SAML — PO kararı K-6, ilk sürüm kapsamı dışı.
- Parola karmaşıklık/geçmiş/periyodik değişim politikası — ayrı iş paketi.
- Alan bazlı görünürlük kuralları — mevcut `IDataScopeService`/`AuthorizationBehaviour` altyapısı zaten var,
  bu fazda yeni kural eklenmedi (FE ikizi API'nin döndürdüğü alanlara göre görünürlüğü uygular).
- Owner hesap devri (tenant'tan ayrılan Owner'ın hesap sahipliğinin devri) — doğrulandı, bugün yok; minimal devir
  komutu tasarımı zaman kalırsa sonraki tura bırakıldı.
