# senseik-051 — Yönetici/İK rolleri için 2FA zorunluluk politikası — UYGULAMA NOTU

## Durum

**Uygulandı, `a/mfa-enforcement-policy` branch'inde, yerel commit alındı (push YAPILMADI).** GATE 1/2 zaten onaylanmıştı; bu tur doğrudan implementasyon.

## Ne değişti

Mevcut 2FA altyapısı (profil sayfasındaki "Etkinleştir" — `EnrollMfaCommand`/`VerifyMfaEnrollmentCommand`/`DisableMfaCommand`, `MeController.mfa/*`) hiç değiştirilmedi. Üstüne, erişim ayarlarıyla (`TenantAccessSettings`) birebir aynı desende yeni bir **zorunluluk katmanı** eklendi.

### Backend

1. **`src/Modules/Identity/Sense.Modules.Identity.Domain/Tenants/TenantSecurityPolicy.cs`** (yeni) — `RequireMfaForRoles: IReadOnlyList<string>` (varsayılan boş) + `RequiresMfaSetup(userRoleCodes, mfaEnabled)` karar metodu (zorunlu rol VE MFA kapalı → true; rol kodu karşılaştırması `OrdinalIgnoreCase`).
2. **`Tenant.cs`** — `SecurityPolicy` alanı (AccessSettings ile aynı gerekçeyle paylaşılan `Default` referansı atanmıyor) + `UpdateSecurityPolicy(...)`.
3. **`IdentityDbContext.cs`** — `SecurityPolicyColumn = "security_policy"`, `OwnsOne(x => x.SecurityPolicy, a => a.ToJson(...))` (AccessSettings ile aynı tek-jsonb-kolon kalıbı).
4. **Migration** `20260919021746_AddTenantSecurityPolicy` — `tenants.security_policy jsonb NOT NULL DEFAULT '{}'` (Docker SDK konteyneri + `dotnet-ef`, startup project `Sense.Migrator` — `Identity.Infrastructure`'ın kendisi `Microsoft.EntityFrameworkCore.Design`'a referans vermiyor, `AddTenantAccessSettings` de muhtemelen aynı yoldan üretilmişti).
5. **`Application/Tenants/SecurityPolicyUseCases.cs`** (yeni) — `GetSecurityPolicyQuery` (`identity.tenant.view`, AccessSettings ile aynı okuma yetkisi) + `UpdateSecurityPolicyCommand` (`identity.security.manage` — bu izin zaten kataloğa ve `SharedResource*.resx`'e tanımlıydı ama hiçbir komuta bağlı değildi; şimdi gerçek bir kullanım alanı var).
6. **`Api/Controllers/AccessSettingsController.cs`** — `SecurityPolicyController` eklendi: `GET/PUT /v1/identity/settings/security`.
7. **`Dtos.cs` / `GetMe.cs`** — `FeaturesDto`'ya `MfaSetupRequired` alanı eklendi. `GetMeHandler` normal (impersonation olmayan) dalda `tenant.SecurityPolicy.RequiresMfaSetup(userRoles.Select(r => r.Code), user.MfaEnabled)` hesaplıyor; impersonation dalında her zaman `false` (destek ekibi platform kullanıcısı bu kiracıda gerçek bir role sahip değil, `AuthorizationBehaviour`'ın tam-erişim bypass'ıyla tutarlı).

Yeni permission kaydı gerekmedi: `identity.security.manage` zaten `IdentityPermissions.All` kataloğunda ve Owner/Admin/ITAdmin joker izin setleri (`*`, `identity.*`) üzerinden bu izne zaten sahipler; `SystemRolePermissionSynchronizer` mevcut tenant'lara ekstra bir şey senkronize etmesi gerekmiyor.

### Frontend

8. **`services/security-policy.service.ts`** (yeni) — `getSecurityPolicy`/`updateSecurityPolicy`, `access-settings.service.ts` ile aynı GET/PUT deseni.
9. **`pages/settings/security-policy.tsx`** (yeni) — Ayarlar → Sistem ayarları → Güvenlik politikası: `listRoles()`'tan gelen tüm rolleri listeler, her biri için "2FA zorunlu" switch'i. Okuma `identity.tenant.view`/`identity.security.manage` (`useAnyPermission`), düzenleme yalnız `identity.security.manage`. `access-settings.tsx` ile birebir aynı iskelet (dirty-check, kaydet/iptal, `refreshMe()`).
10. **`config/navigation.ts`** — `SETTINGS_PERMISSIONS.securityPolicy` + "Sistem ayarları" altına yeni nav linki (`/app/settings/system/security-policy`, `Lock` ikonu).
11. **`App.tsx`** — lazy route eklendi.
12. **`types/index.ts`** / **`store/auth.store.ts`** — `MeFeatures.mfaSetupRequired` (yoksa `false`'a düşer), `SecurityPolicy` tipi.
13. **`components/mfa-setup-guard.tsx`** (yeni) — asıl "zorunlu kılınan rolde 2FA kapalıyken kritik ekranlara erişmeden önce kurulum akışına yönlendirme" guard'ı. `layouts/app-layout.tsx`'te `<Outlet />` yerine geçti: sidebar/header her zaman yerinde kalır (kullanıcı hesap sayfasından çıkış yapabilir), ama `features.mfaSetupRequired` true iken `/app/account` dışındaki her rota `<Navigate replace>` ile hesap sayfasına yönlendirilir — orada zaten var olan MFA kurulum akışı (Etkinleştir → QR/secret → kod doğrulama) devreye giriyor. Büyük bir yeniden tasarım değil: tek bir küçük route guard bileşeni.
14. **Yerelleştirme** — `tr/en/de` `navigation.json` (`settings.securityPolicy(Desc)`) ve `settings.json` (`securityPolicy.*`).

## Neden bu tasarım

- Kart `TenantSecurityPolicy.RequireMfaForRoles: string[]` örneğini birebir istiyordu; rol kodu listesi (Owner/Admin/HR/PayrollSpecialist vb.) sabit kodlanmadı, `listRoles()` üzerinden tenant'ın gerçek rol kataloğu (özel roller dahil) gösteriliyor — ileride özel bir role de 2FA zorunlu kılınabilir.
- Zorunluluk kararı **backend'de** (`GetMeHandler`) hesaplanıp `/identity/me` üzerinden tek bir `mfaSetupRequired` booleanı olarak taşınıyor; ön yüz roller/politikayı kendi başına birleştirmiyor — sunucu tarafı tek doğruluk kaynağı.
- Guard, mevcut auth akışına (`ProtectedRoute` → `AppLayout`) eklenen tek bir küçük bileşen; `ProtectedRoute`'un kendisine dokunulmadı, `AccountPage`'e dokunulmadı.

## Bilinçli sınır (kapsam dışı)

Bu bir **ön yüz** yönlendirme guard'ı — backend tarafında zorunlu-ama-MFA'sız bir kullanıcının diğer API uçlarını çağırmasını engelleyen bir sunucu-taraflı middleware/authorization filter EKLENMEDİ (kart "basit bir middleware/guard... büyük bir yeniden tasarım değil" diyor ve kabul kriterleri özellikle "ekranlara erişim"den bahsediyor). Yani API doğrudan çağrılırsa (Postman vb.) zorunluluk bypass edilebilir. Gerçek bir yetkilendirme sınırı isteniyorsa, ayrı bir kart olarak `AuthorizationBehaviour`'a (veya bir `ActionFilter`'a) `[RequiresPermission]` ile aynı seviyede bir `RequiresMfaSetupComplete` kontrolü eklenmeli — bu turun kapsamına dahil edilmedi.

## Testler

**Backend** (Docker SDK konteyneri, `dotnet test`):
- `tests/Modules/Sense.Modules.Identity.Tests/Tenants/TenantSecurityPolicyTests.cs` (yeni, 7 test) — varsayılan değer, `Tenant.UpdateSecurityPolicy`, `RequiresMfaSetup` karar mantığı (zorunlu rol + MFA kapalı → true; case-insensitive; MFA açıkken false; rol listede değilken false; politika boşken false).
- `tests/Modules/Sense.Modules.Identity.Tests/Tenants/SecurityPolicyUseCasesTests.cs` (yeni, 4 test) — `GetSecurityPolicyHandler`/`UpdateSecurityPolicyHandler` round-trip + kiracı izolasyonu (AccessSettingsUseCasesTests ile aynı desen).
- `tests/Modules/Sense.Modules.Identity.Tests/Auth/GetMeHandlerTests.cs` (3 yeni test eklendi) — **asıl "backend policy testi"**: zorunlu rol + MFA kapalı → `Features.MfaSetupRequired == true`; zorunlu rol + MFA açık → `false`; rol politikada zorunlu değilse → `false`.
- Sonuç: `Sense.Modules.Identity.Tests` tam koşusu **111/111 geçti** (regresyon yok).
- `dotnet build src/Sense.Api/Sense.Api.csproj` → **Build succeeded, 0 hata** (yeni controller/DI derleniyor).

**Frontend** (Docker `node:24-alpine`, `pnpm --filter web`):
- `src/components/mfa-setup-guard.test.tsx` (yeni, 4 test) — **asıl "frontend yönlendirme testi"**: `mfaSetupRequired=true` iken başka bir rotadan hesap sayfasına yönlendiriyor; zaten hesap sayfasındayken yönlendirmiyor; `mfaSetupRequired=false` iken normal render; `features` henüz yüklenmemişken (null) engellemiyor.
- `src/services/security-policy.service.test.ts` (yeni, 4 test) — GET/PUT endpoint'leri + hata yayılımı.
- `pnpm exec tsc --noEmit` → temiz.
- `pnpm exec eslint` (değişen dosyalar) → temiz.
- `pnpm exec vitest run` (yukarıdaki iki dosya + regresyon için `access-settings.service.test.ts`) → **20/20 geçti**.

## Kritik Dosyalar

- `src/Modules/Identity/Sense.Modules.Identity.Domain/Tenants/TenantSecurityPolicy.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Domain/Tenants/Tenant.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Infrastructure/Persistence/Migrations/20260919021746_AddTenantSecurityPolicy.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Application/Tenants/SecurityPolicyUseCases.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Application/Auth/GetMe.cs`, `Dtos.cs`
- `src/Modules/Identity/Sense.Modules.Identity.Api/Controllers/AccessSettingsController.cs` (`SecurityPolicyController` aynı dosyada)
- `web/src/components/mfa-setup-guard.tsx`, `web/src/layouts/app-layout.tsx`
- `web/src/pages/settings/security-policy.tsx`, `web/src/services/security-policy.service.ts`
- `web/src/config/navigation.ts`, `web/src/App.tsx`, `web/src/types/index.ts`, `web/src/store/auth.store.ts`

---
*Kaynak: `docs/agent-board/cards/senseik-051-mfa-enforcement-policy.json` (1. Tur Test Bulgu Dokümanı § E-03).*
