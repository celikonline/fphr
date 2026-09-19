# Handoff — senseik-057 Ayarlar → Modüller (modül aç/kapa)

Branch `a/module-toggle-settings`, worktree `.claude/worktrees/module-toggle`.

## Ne yapıldı

**Katalog (tek kaynak)** — `src/Shared/Sense.Shared.Contracts/Modules/ProductModules.cs`
- Temel (kapatılamaz): identity, organization, employee, workflow, notifications, documents, dataTransfer, reporting, analytics, integrations.
- Kapatılabilir: leave, attendance, payroll, recruitment, expense, assets, performance, announcements, occupationalSafety.
- `FromAssemblyName("Sense.Modules.Leave.Api")` → leave. `IModuleAccessService`, `ModuleAccessExemptAttribute`, hata kodları (`module.disabled`, `module.unknown`, `module.not_toggleable`).
- Web aynası: `web/src/config/modules.ts` (anahtarlar + rota önekleri → modül).

**Identity (depolama + API)**
- `Tenant.ModuleSettings` (`TenantModuleSettings`, jsonb `identity.tenants.module_settings`, KAPALI anahtar listesi → yeni modüller varsayılan açık). Migration `20260919111354_AddTenantModuleSettings` (default `{}`).
- `Application/Tenants/ModuleSettingsUseCases.cs`: `GetModuleSettingsQuery` (identity.tenant.view), `UpdateModuleSettingsCommand` (yeni izin `identity.modules.manage`; Owner/Admin `*` joker ile alır, HR/diğerleri almaz). Temel/bilinmeyen anahtar → 400 validation (yerelleştirilmiş, `{module}` argümanı).
- `ModuleSettingsController`: `GET/PUT /api/v1/identity/settings/modules` (body `{ "disabledModules": [...] }`).
- `/identity/me` → `features.disabledModules` (herkes için aynı, bypass yok; impersonation dahil).
- `Infrastructure/Security/ModuleAccessService.cs`: HybridCache (`modules-disabled-v1:{tenant}`, `Caching:DefaultExpirationMinutes`), PUT handler'ı `IModuleSettingsCacheInvalidator` ile geçersizler.

**API zorlaması** — `src/Shared/Sense.Shared.Web/Authorization/ModuleAccessFilter.cs` (global resource filter, `AddSenseWeb`'de kayıtlı)
- Controller assembly'si kapatılabilir bir modülse ve kiracı kapattıysa → 403 ProblemDetails `code=module.disabled`.
- Asla engellenmez: temel modüller (Modüller ayar uçları Identity'de), katalog dışı assembly'ler, kiracı bağlamı olmayan istekler (anonim/arka plan), `[ModuleAccessExempt]` (Announcements içindeki `SupportController`).
- Yetkilendirme filtrelerinden sonra, model binding'den önce çalışır.

**Web**
- Sayfa `web/src/pages/settings/modules.tsx` (`/app/settings/system/modules`, Ayarlar hub'ında "Sistem ayarları" altında). Görüntüleme `identity.tenant.view`, değiştirme `identity.modules.manage`; kapatmada onay diyaloğu ("Veriler silinmez, sadece gizlenir"); temel modüller "Temel modül" kilitli rozetiyle.
- `useVisibleItems` artık `path`'e göre kapalı modül öğelerini gizler → sidebar, uygulama başlatıcı (header + /app/apps), ayarlar kartları otomatik.
- `ModuleRouteGuard` (app-layout): kapalı modül rotası "Bu modül kapalı" sayfası gösterir (yer imi/eski link çökmez).
- Ana sayfa izin/etkinlik widget'ları, çalışan kartı İzin/Fazla mesai/Zimmet sekmeleri, Kariyer > Çalışma takvimi bloğu (attendance), işten çıkışta açık zimmet uyarısı (assets) ve kıdem tahmini (payroll) gizlenir.
- Kaydetten sonra `refreshMe()`; diğer kullanıcılar sayfa yenilemede/uygulama açılışında görür.
- Locale: tr/en/de `settings.modules.*`, `navigation.settings.modules{,Desc}`.

## Kapsam dışı / bilinen sınırlar
- **Arka plan işleri ve olay handler'ları kapalı modüllerde çalışmaya devam eder** (Hangfire işleri, outbox/integration event tüketicileri, bildirim üreten zamanlanmış işler). Veri tutarlılığı için kasıtlı; kapalı bir modül e-posta/bildirim üretebilir.
- Temel modüllerin kapalı modül verisini okuyan uçları (Reporting/Analytics raporları, Workflow onay kutusundaki izin talepleri, Contracts üzerinden okunanlar) engellenmez.
- Anonim uçlar (kariyer sayfası `/kariyer`, `recruitment/public/postings`) kiracı bağlamı çözülmediğinden işe alım kapalıyken de açıktır.
- Çok instance'lı dağıtımda diğer node'ların L1 önbelleği en fazla `Caching:LocalExpirationSeconds` (30 sn) eski kalabilir; geçersizleme UoW commit'inden önce çalışır (çok kısa bir yarış penceresi; üst sınır DefaultExpirationMinutes).
- Web: diğer oturumlar `/me` yenilenene kadar (yeniden yükleme/giriş) eski menüyü görür; API zaten 403 döner.
- **Mobil (apps/senseik-mobile)**: yapılmadı — mobil `/identity/me` `features` kullanmıyor (`/api/users/me`), trivial değil. Takip: mobil auth'u `/identity/me`'ye bağlayıp Leave/Attendance sekmelerini `disabledModules`'a göre gizlemek.
- Testcontainers entegrasyon testleri (`tests/Sense.Tests.Integration/ModuleToggleIntegrationTests.cs`: round-trip, 403 filtresi/temel/muaf/yeniden açma, kiracı izolasyonu, eski `{}` satırı, katalog tamlığı) derleniyor ama Docker bu makinede yanıt vermediği için **koşulmadı** — merge öncesi Docker açık bir makinede koşulmalı. Özellikle `LegacyRow_WithEmptyJsonObject_ReadsAsAllEnabled` migration default'unun (`{}`) EF tarafından okunabildiğini doğrular (TenantSecurityPolicy aynı kalıbı kullanıyor).

## Doğrulama
- `dotnet build src/Sense.Api` 0 hata; `tests/Sense.Tests.Integration` derleniyor.
- Identity testleri (container dışı) 130/130; Architecture 56/56.
- Web: `tsc --noEmit` yalnız bilinen 4 hata (protected-route.test.tsx / home.test.tsx); `eslint src` 0 hata; vitest ilgili dosyalar 221/222 (tek başarısız: önceden var olan `de/attendance.json` placeholder testi, bu dalda dokunulmadı).
