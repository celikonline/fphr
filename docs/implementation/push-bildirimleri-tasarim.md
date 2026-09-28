# Push bildirimleri (Expo) — tasarım + şema değişikliği isteği

Bu doküman bir **tasarım + şema değişikliği isteği**dir; kod/migration üretmez. Takım kuralı
(`.claude/rules/takim-riskli-alan.md`): "Migration elle yazılmaz ya da değiştirilmez: gereken şema
değişikliği istek olarak rapora yazılır, üretimi insan yapar." §1'deki entity/migration **insan
tarafından üretilecek**; bu belge yalnız taslak ve gerekçedir.

## 0. Bağlam ve kapsam

Mobil uygulama (Expo SDK 57, `apps/senseik-mobile`) `expo-notifications` ile bir Expo push token
alıyor ama gönderecek uç yok:
`apps/senseik-mobile/src/services/PushNotificationService.ts:59-70` var olmayan
`/api/users/{id}/push-tokens`'a POST ediyor, 404'ü sessizce yutuyor (bkz. dosyadaki B1 yorumu).

Notifications modülü (`src/Modules/Notifications/**`) bugün iki kanalı destekliyor:
e-posta (`notification_deliveries`, `EmailDeliveryProcessor`) ve uygulama içi
(`user_notifications`, SignalR). `NotificationChannels` (Contracts, satır 58-65) şu an
`Email`/`InApp`; sınıf yorumu "Push (Expo/Web Push) is a later phase" diyor — bu doküman o fazın
tasarımı. Web Push (tarayıcı) bu dokümanın kapsamı dışında; yalnız Expo (mobil) push ele alınıyor.

**Tasarım kararı (özet):** yeni bir "push outbox" tablosu açmak yerine mevcut
`notification_deliveries` tablosu push kanalı için de kullanılıyor (bkz. §3), yalnızca yeni bir
**cihaz/token tablosu** (`push_device_tokens`) ekleniyor. Bu, mevcut idempotency/backoff/worker
altyapısını (§3'te detaylandırılan tek önemli düzeltme hariç) olduğu gibi yeniden kullanır.

---

## 1. Şema değişikliği isteği (RİSKLİ ALAN — insan üretecek)

**Riskli dosyalar:** `src/Modules/Notifications/Sense.Modules.Notifications.Infrastructure/Persistence/Migrations/**`
zaten takım riskli-alan listesinde. Aşağıdaki entity/configuration/migration **elle yazılmadı**;
insan onayından sonra üretilmeli.

### 1.1 Yeni tablo: `notifications.push_device_tokens`

Bir kullanıcının bir fiziksel cihazdaki Expo push token'ı. Aynı token aynı anda yalnız bir
kullanıcıya ait olabilir (bir cihaz logout/login ile başka bir çalışana geçebilir — token cihaza
aittir, kullanıcıya değil); bu yüzden **token üzerinde `revoked_at IS NULL` koşullu (partial)
benzersiz indeks** gerekiyor, `(tenant_id, user_id)` üzerinde değil.

| Kolon | Tip | Not |
|---|---|---|
| `id` | uuid PK | `TenantEntity<Guid>` tabanı |
| `tenant_id` | uuid | `TenantEntity<Guid>` tabanı (audit interceptor zaten dolduruyor) |
| `user_id` | uuid | Identity kullanıcı id'si (Employee değil — bildirimler zaten `UserId` üzerinden gidiyor, bkz. `NotificationRecipient.UserId`) |
| `token` | varchar(255) | Expo push token (`ExponentPushToken[...]`) |
| `platform` | varchar(20) | `ios` \| `android` |
| `device_name` | varchar(100) NULL | Opsiyonel (`expo-device` `Device.deviceName`), sadece kullanıcının "cihazlarım" listesinde göstermek için — PII değil (kullanıcı kendi cihaz adını görüyor) |
| `created_at`, `created_user_id`, `modified_date`, `modified_user_id` | — | `Entity<TId>`/audit interceptor zaten sağlıyor, ayrı kolon eklenmiyor |
| `last_seen_at` | timestamptz | Token her doğrulandığında/yeniden kaydedildiğinde güncellenir (uygulama her açılışta `registerPushToken` çağırıyor) |
| `revoked_at` | timestamptz NULL | Logout'ta veya Expo `DeviceNotRegistered` yanıtında set edilir |

İndeksler:
- Benzersiz, kısmi: `token` üzerinde `WHERE revoked_at IS NULL` (aynı anda bir token → en çok bir aktif satır).
- `(tenant_id, user_id) WHERE revoked_at IS NULL` (worker'ın "bu kullanıcının aktif cihazları" sorgusu için).

### 1.2 Entity taslağı (Domain) — üretilmedi, taslak

```csharp
// src/Modules/Notifications/Sense.Modules.Notifications.Domain/PushDevices/PushDeviceToken.cs
namespace Sense.Modules.Notifications.Domain.PushDevices;

/// <summary>
/// A device's Expo push token for one user. A token belongs to a physical device, not a user: if the same
/// device logs out and a different employee logs in, the old row must be revoked (see RevokeToken) before the
/// new user's row is inserted, otherwise both accounts would receive pushes on that device. Only one row per
/// token may be active (RevokedAt is null) at a time - enforced by a partial unique index on Token.
/// </summary>
public sealed class PushDeviceToken : TenantEntity<Guid>
{
    private PushDeviceToken()
    {
    }

    public PushDeviceToken(Guid tenantId, Guid userId, string token, string platform, string? deviceName, DateTime now)
        : base(Guid.CreateVersion7(), tenantId)
    {
        UserId = userId;
        Token = Guard.MaxLength(Guard.NotEmpty(token), NotificationsLimits.PushTokenMaxLength);
        Platform = Guard.MaxLength(Guard.NotEmpty(platform), NotificationsLimits.PushPlatformMaxLength);
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : Guard.MaxLength(deviceName, NotificationsLimits.DeviceNameMaxLength);
        LastSeenAt = now;
    }

    public Guid UserId { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public string Platform { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Idempotent re-registration (app foreground, token unchanged).</summary>
    public void Touch(DateTime now) => LastSeenAt = now;

    /// <summary>Logout, or Expo reported DeviceNotRegistered for this token.</summary>
    public void Revoke(DateTime now) => RevokedAt ??= now;
}
```

`NotificationsLimits.cs`'e eklenecek sabitler (mevcut dosyaya ek, migration değil):

```csharp
public const int PushTokenMaxLength = 255;
public const int PushPlatformMaxLength = 20;
public const int DeviceNameMaxLength = 100;
```

### 1.3 EF configuration taslağı — mevcut `NotificationsDbContext.cs`'teki desenle birebir

```csharp
// NotificationsDbContext.cs içine:
public DbSet<PushDeviceToken> PushDeviceTokens => Set<PushDeviceToken>();

// NotificationsTables içine:
public const string PushDeviceTokens = "push_device_tokens";

public sealed class PushDeviceTokenConfiguration : IEntityTypeConfiguration<PushDeviceToken>
{
    public void Configure(EntityTypeBuilder<PushDeviceToken> b)
    {
        b.ToTable(NotificationsTables.PushDeviceTokens);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Token).HasMaxLength(NotificationsLimits.PushTokenMaxLength).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(NotificationsLimits.PushPlatformMaxLength).IsRequired();
        b.Property(x => x.DeviceName).HasMaxLength(NotificationsLimits.DeviceNameMaxLength);

        // A token belongs to one device; only one active (non-revoked) owner at a time.
        b.HasIndex(x => x.Token).IsUnique().HasFilter("revoked_at IS NULL");

        // Worker fan-out: this user's active devices.
        b.HasIndex(x => new { x.TenantId, x.UserId }).HasFilter("revoked_at IS NULL");
    }
}
```

(`HasFilter` SQL'i tırnaklama EF Core'un Npgsql sağlayıcısında snake_case kolon adını beklediği
için `revoked_at` yazıldı — proje `UseSnakeCaseNamingConvention()` kullanıyor, bkz.
`NotificationsDbContextFactory.cs:14`. İnsan üretirken gerçek kolon adını doğrulamalı.)

### 1.4 Migration üretim komutu (insan çalıştıracak)

Reponun gerçek deseni (bkz. `build/new-module.ps1:325-327`, `docs/handoffs/audit-log-coverage-expansion.md:43`):

```bash
docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet ef migrations add AddPushDeviceTokens \
  --project src/Modules/Notifications/Sense.Modules.Notifications.Infrastructure \
  --startup-project src/Sense.Migrator \
  -o Persistence/Migrations
```

`Sense.Migrator` zaten `NotificationsDbContext`'i `AddModuleDbContext<NotificationsDbContext>` ile
biliyor (`AddNotificationsCore` üzerinden) — context adı tek olduğu için `-c` gerekmiyor.

---

## 2. API sözleşmesi

Yeni uçlar `MyNotificationsController` içine eklenir (route zaten `api/v{version}/notifications/my`).

### 2.1 `POST /api/v1/notifications/my/devices`
- **Yetki:** `notifications.my.manage` (**yeni izin anahtarı açılmıyor** — bu anahtar zaten
  `notifications.my.*` deseniyle `PermissionPatterns.SelfService` üzerinden her temel role
  ulaşıyor, bkz. `NotificationsPermissions.cs:9` yorumu ve `UpdateMyNotificationPreferencesCommand`
  aynı izni kullanıyor. `SystemRoleDefinitions`/`SystemRolePermissionSynchronizer` değişikliği gerekmez.)
- **Gövde:** `{ token: string, platform: "ios" | "android", deviceName?: string }`
- **Yanıt:** `204 No Content`
- **Davranış: idempotent upsert.**
  - Aynı token, aynı kullanıcı, aktif satır → `LastSeenAt` güncellenir (`Touch`), yeni satır açılmaz.
  - Aynı token, **başka** bir kullanıcıya ait aktif satır varsa (cihaz el değiştirmiş / farklı
    çalışan giriş yapmış) → eski satır `Revoke` edilir, yeni kullanıcı için yeni satır açılır
    (token rotasyonu — aksi halde iki hesap da aynı cihaza push alır).
  - Hiç satır yok → yeni satır.
- **Hatalar:** `notifications.device.invalid_token` (400, boş/uzun token),
  `notifications.device.invalid_platform` (400, `ios`/`android` dışı bir değer).
- CQRS taslağı (`PreferenceRequests.cs` deseniyle):
  ```csharp
  [RequiresPermission("notifications.my.manage")]
  public sealed record RegisterMyPushDeviceCommand(string Token, string Platform, string? DeviceName) : ICommand;
  ```

### 2.2 `DELETE /api/v1/notifications/my/devices/{token}`
- **Yetki:** `notifications.my.manage`
- **Yanıt:** `204 No Content` — **idempotent** ve token başkasına aitse de `204` döner (var/yok
  bilgisini sızdırmamak için; sadece çağıranın kendi aktif token'ı revoke edilir, eşleşme yoksa no-op).
- Logout akışında çağrılır (bkz. §4).
- **Route notu:** Expo token'ları köşeli parantez içerir (`ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxxxxxx]`).
  Path segment olarak taşınacaksa mobil taraf `encodeURIComponent(token)` ile göndermeli;
  ASP.NET routing `[` `]` karakterlerini path segment içinde sorunsuz kabul eder ama ters kesin
  bunu bir entegrasyon testiyle doğrulamalı. Alternatif (daha güvenli): token'ı body'de taşıyan bir
  `POST .../devices/revoke` — insan/gözden geçirici tercih ederse bu tasarım değişir.
- CQRS taslağı:
  ```csharp
  [RequiresPermission("notifications.my.manage")]
  public sealed record RemoveMyPushDeviceCommand(string Token) : ICommand;
  ```

---

## 3. Gönderim tasarımı (Worker/outbox üzerinden — API isteğinde asla değil)

### 3.1 Yeni kanal

`NotificationChannels` (Contracts) içine üçüncü kanal eklenir:

```csharp
public static class NotificationChannels
{
    public const string Email = "email";
    public const string InApp = "in_app";
    public const string Push = "push";

    public static IReadOnlyList<string> All { get; } = [Email, InApp, Push];
}
```

`NotificationCatalog.cs`'te hangi şablonların push'u destekleyeceği bir ürün kararı; öneri (ilk
faz, engagement/zaman-duyarlı olanlar): `ApprovalTaskAssigned`, `RequestApproved`, `RequestRejected`,
`PayslipReady`, `AnnouncementPublished`, `EmployeeBirthday`. Diğerleri (`EmployeeWorkCertificate`,
`OhsSgkNotificationDue` gibi zorunlu/hassas olanlar) ilk fazda push almaz — bu bir öneri, kesin
liste ürün/insan kararı.

### 3.2 Neden yeni bir "push outbox" tablosu yok

`notification_deliveries` zaten "Pending satır → worker claim eder → gönderir → Sent/Failed" akışını
(idempotency key, exponential backoff, `FOR UPDATE SKIP LOCKED`) sağlıyor. Push için aynı satır
tipini `Channel = "push"` ile kullanmak öneriliyor:
- `RecipientAddress` = **`userId.ToString()`** (e-posta adresi değil) — bu satır kullanıcının o an
  aktif olan **tüm** cihazlarını temsil eder; hangi cihazlara gönderileceği gönderim anında
  `push_device_tokens`'tan okunur (yayın anında değil). Böylece kullanıcı yeni bir cihaz eklerse/
  bir cihazını kaybederse publish zamanı değil, gönderim zamanı doğru cihaz seti kullanılır.
  `SourceEventId + TemplateKey + Channel + RecipientAddress` benzersizliği aynen korunur (bir
  kullanıcı için bir olayda bir push satırı).
- `AddressSource` = `"user_id"` (yeni bir `AddressSources` sabiti, PII değil).

**ÖNEMLİ — mevcut kodda gerekli düzeltme (migration değil, normal kod değişikliği, ayrı bir iş
kartında yapılmalı):** `EmailDeliveryProcessor.ProcessAsync`'teki tarama sorgusu
(`EmailDeliveryProcessor.cs:40-47`) bugün **kanal filtresi içermiyor** —
`Where(d => d.Status == Pending && d.NextAttemptAt <= now)`. Tek kanal (`email`) olduğu için bugün
zararsız, ama `push` satırları aynı tabloya eklenir eklenmez `EmailDeliveryProcessor` onları da
claim edip SMTP göndericisine bir kullanıcı id'sini "e-posta adresi" olarak vermeye çalışacaktır.
Bu sorguya `&& d.Channel == NotificationChannels.Email` eklenmesi **push işine bağımlı, zorunlu bir
ön koşuldur** — insan/gözden geçirici bunu ayrı bir küçük düzeltme commit'i olarak ele almalı.

### 3.3 `NotificationPublisher` entegrasyonu (taslak, `NotificationPublisher.cs:63-85` deseninde)

```csharp
if (recipient.UserId is { } pushUserId && Enabled(pushUserId, NotificationChannels.Push)
    && !await deliveries.ExistsAsync(request.SourceEventId, request.TemplateKey, NotificationChannels.Push, pushUserId.ToString(), ct))
{
    deliveries.Add(new NotificationDelivery(
        tenantId, request.SourceEventId, request.TemplateKey, NotificationChannels.Push,
        recipient.EmployeeId, pushUserId, pushUserId.ToString(), recipient.DisplayName,
        AddressSources.UserId, recipient.Language, JsonSerializer.Serialize(model, JsonOptions), now));
}
```

### 3.4 Yeni Worker bileşenleri (`EmailDeliveryProcessor`/`EmailDeliveryPollingService` ile birebir ayna)

- `IPushDeviceTokenRepository` (Domain, `Repositories.cs` deseninde): `ListActiveForUserAsync(userId)`, `RevokeAsync(token, now)`.
- `PushDeliveryProcessor`: `db.Deliveries.Where(d => d.Channel == NotificationChannels.Push && d.Status == Pending && d.NextAttemptAt <= now)` ile tarar, aynı `ClaimById` SQL'iyle (id zaten filtrelenmiş satırdan geliyor, kanal ayrımı gerekmiyor) satırı kilitler, `PushDeliverySender`'a devreder.
- `PushDeliverySender`: `delivery.RecipientAddress` (= userId) için aktif token'ları okur; hiç aktif cihaz yoksa `delivery.MarkSkipped("no_active_device")` (hata değil — kullanıcı hiç mobil kaydı yapmamış olabilir). Varsa, rendered başlık/gövde için **§3.1'de zaten hesaplanmış in-app metnini** yeniden kullan (ayrı `{key}.push.{lang}.sbn` şablonları açmaya gerek yok — `UserNotification`'ın Title/Body'si zaten hedef dilde kısa metin). Expo'ya tüm aktif cihazlar için tek istekte (bir kullanıcının cihaz sayısı tipik olarak 1-3, Expo'nun 100 mesajlık batch limitinin çok altında) gönderir; `MarkSent`/`RecordFailure` mevcut state machine'i (backoff dahil) aynen kullanılır.
- `PushDeliveryPollingService`: `EmailDeliveryPollingService` ile birebir aynı `BackgroundService` iskeleti, kendi `PollingIntervalSeconds`/`BatchSize` ile (aşağıda).
- DI: `NotificationsServiceCollectionExtensions.AddNotificationsCore` içine `services.AddScoped<IPushDeviceTokenRepository, PushDeviceTokenRepository>()`, `services.AddScoped<PushDeliverySender>()`, `services.AddScoped<PushDeliveryProcessor>()`, `services.AddHttpClient<IExpoPushSender, ExpoPushSender>(c => { c.BaseAddress = new Uri("https://exp.host/"); c.Timeout = TimeSpan.FromSeconds(10); })` (Integrations modülündeki `services.AddHttpClient(WebhookHttpClientNames.Default, ...)` deseniyle aynı). `Sense.Worker/Program.cs`'e `builder.Services.AddHostedService<PushDeliveryPollingService>()` (satır 136 civarı, `EmailDeliveryPollingService` ile yan yana).

### 3.5 `IExpoPushSender` — Expo Push API istemcisi

`POST https://exp.host/--/api/v2/push/send`, gövde bir mesaj dizisi (≤100/istek):
```json
[{ "to": "ExponentPushToken[...]", "title": "...", "body": "...", "data": { "type": "approval.task_assigned", "id": "<guid>" }, "priority": "high" }]
```
- **Payload'da yalnız `templateKey` (`data.type`) + güvenli bir id** (ör. `UserNotification.Id` —
  onaya/talebe değil, kullanıcının kendi bildirim satırına işaret eder) taşınır; başlık/gövde metin
  olarak zaten Türkçe/İngilizce/Almanca render edilmiş haliyle gönderilir (PII: isim `Body` içinde
  olabilir — bu bugün de e-posta/in-app için geçerli, push için yeni bir risk değil, ama push
  bildirimi kilit ekranında görünebileceği için **isim/rakam içermeyen daha kısa bir push metni**
  ürün kararı olarak değerlendirilmeli — bu doküman bunu bir açık konu olarak işaretliyor).
- `data.type` mobilin **beyaz listesiyle** (`apps/senseik-mobile/src/lib/pushNavigation.ts:16-36`,
  `ALLOWED_PUSH_SCREENS` + `NOTIFICATION_TYPE_SCREENS`) uyumlu olmalı — yani sunucu tarafında yeni
  bir push-etkin şablon eklenirken mobildeki bu whitelist'in de güncellenmesi gerekir (ayrı, küçük
  bir mobil iş; risk: whitelist'te olmayan bir `type` sessizce hiçbir yere navigate etmez, güvenli
  taraf zaten budur).
- **Yanıt işleme:** Expo her mesaj için bir "ticket" döner; `status: "error"`,
  `details.error: "DeviceNotRegistered"` → o token için `PushDeviceTokenRepository.RevokeAsync`.
  Diğer hatalar (`MessageTooBig`, `MessageRateExceeded`, `InvalidCredentials`) →
  `delivery.RecordFailure` (mevcut backoff). Expo'nun asıl teslim durumunu döndüren "receipt" ucu
  (`/--/api/v2/push/getReceipts`) ilk fazda kapsam dışı bırakılabilir — yalnız ticket seviyesinde
  hata işleme yeterli (ürün/insan kararı, ölçek büyürse eklenir).
- Yeni `NotificationsOptions.Push` bölümü (`Delivery` ile aynı şekil): `BatchSize`, `MaxAttempts`,
  `BaseBackoffSeconds`, `MaxBackoffSeconds`, `PollingIntervalSeconds` — push'un e-postadan daha sık
  taranması (ör. 2 sn) önerilir, ayrı config anahtarı olduğu için bu bir kod değişikliği, migration değil.

---

## 4. Mobil taraf (`apps/senseik-mobile`) — riskli alanla kesişmiyor, ayrı iş

`PushNotificationService.ts` değişecek yerler (bu doküman değiştirmiyor, sadece işaret ediyor):
- `uploadPushToken` → yeni uca (`POST /api/v1/notifications/my/devices`) `{ token, platform: Platform.OS, deviceName }` gönderecek şekilde güncellenir; `type: "expo"` alanı kaldırılır (uç zaten sadece Expo token bekliyor). B1 yorumu (satır 56-58) kaldırılır.
- Uç artık var olduğu için sessiz-yutma davranışı (satır 65-69) gözden geçirilmeli: 4xx (geçersiz token/platform) log'lanmadan yutulmaya devam edebilir, ama artık gerçek bir başarı/başarısızlık ayrımı mümkün.
- **Logout'ta silme:** `DELETE /api/v1/notifications/my/devices/{token}` çağrısı logout akışına eklenmeli. Bu akış `apps/senseik-mobile/src/services/AuthService.ts` (logout metodu) ve/veya `apps/senseik-mobile/src/store/authStore.ts` içinde — **her ikisi de riskli alan listesinde**, bu doküman oraya dokunmuyor; ayrı bir iş kartı olarak insan onayıyla yapılmalı. Not: token silme network çağrısı başarısız olsa bile logout'u bloklamamalı (best-effort, `SecureStorage.clearAll()` ile aynı "asla logout'u durdurma" ilkesi, bkz. `storage.ts:202-207`).
- **`EXPO_PUBLIC_PROJECT_ID` / EAS projectId:** bugün `PushNotificationService.ts:30` bir ortam değişkeni (`EXPO_PUBLIC_PROJECT_ID`) okuyor, ama `apps/senseik-mobile/app.json:66-70` içinde `extra.eas.projectId: "senseik-mobile"` zaten tanımlı — bu **gerçek bir EAS proje UUID'si değil, yer tutucu** görünüyor (gerçek EAS project id'leri UUID biçimindedir). Push'un prod'da çalışması için: (a) `eas init`/`eas build:configure` ile gerçek bir EAS projesi açılmalı, (b) ya `EXPO_PUBLIC_PROJECT_ID` bu gerçek id ile set edilmeli, ya da kod `Constants.expoConfig?.extra?.eas?.projectId`'yi okuyacak şekilde değiştirilmeli (iki ayrı yerde aynı değeri tutmak yerine). Bu, insan/DevOps kararı gerektiren bir **açık konu**.

---

## 5. Güvenlik / KVKK

- **Push token kişisel veri sayılır** — bir cihazı (dolaylı olarak bir kullanıcıyı) tekil olarak
  tanımlar (KVKK m.3 "kişisel veri" tanımı, dolaylı kimliklendirme). `docs/implementation/kvkk-imha-kaydi-sozlesmesi.md`
  kapsamındaki imha kaydı akışına push token'lar da eklenmelidir (mevcut sözleşme belgesi riskli
  alanda — bu doküman ona dokunmuyor, sadece işaret ediyor).
- **Loglanmaz:** mevcut mobil kod zaten "Never log the push token" kuralını uyguluyor
  (`PushNotificationService.ts:38`); backend tarafında da `PushDeliverySender`/`ExpoPushSender`
  hiçbir log satırında ham token'ı yazmamalı (mevcut `EmailDeliveryProcessor` de e-posta adresini
  log'lamıyor, aynı ilke — `LoggerMessage` mesajlarında sadece `DeliveryId`/`TemplateKey` geçiyor).
- **Saklama süresi:** aktif token'lar kullanıcı aktif olduğu sürece saklanır (`LastSeenAt`
  güncellenir). Revoke edilmiş (`RevokedAt` dolu) satırlar için öneri: 90 gün sonra kalıcı silme
  (ayrı bir temizlik job'u — bu doküman kesin süreyi belirlemiyor, ürün/KVKK sorumlusu kararı).
  Kullanıcı hesabı silindiğinde/anonim­leştirildiğinde (mevcut KVKK imha akışı) `push_device_tokens`
  satırları da o akışa dahil edilmeli.
- Token, e-posta adresi gibi `RecipientAddress` alanına yazılıyor olsa da (§3.2) bu alanda artık bir
  e-posta değil bir `userId` var — asıl token yalnız `push_device_tokens.token`'da tutuluyor, delivery
  kaydında (`notification_deliveries`) hiç görünmüyor. Bu, token'ın tek bir tabloda toplanmasını ve
  imha/erişim taleplerinde tek yerden yönetilmesini sağlıyor.

---

## 6. İş sırası ve kabul kriterleri

Önerilen sıra (her biri ayrı, küçük iş kartı):
1. **Şema** — §1'deki entity/configuration/migration insan tarafından üretilir. *(Riskli alan.)*
2. **API** — §2 uçları + `PushPlatforms`/`AddressSources.UserId` sabitleri + validator'lar.
3. **Gönderim** — §3: `NotificationChannels.Push`, `NotificationPublisher` entegrasyonu,
   `EmailDeliveryProcessor` kanal filtresi düzeltmesi (**bu iş 3'ün ön koşulu, atlanamaz**),
   `PushDeliveryProcessor`/`PushDeliveryPollingService`/`ExpoPushSender`, Worker DI kaydı.
4. **Mobil** — §4: `PushNotificationService.ts` yeni uca bağlanır, logout entegrasyonu (riskli
   alan dosyalarına dokunduğu için ayrı, insan onaylı iş), EAS project id netleştirilir.
5. **KVKK** — §5: imha kaydı sözleşmesine push token eklenmesi (riskli alan dosyası, ayrı iş).

Kabul kriterleri (üst seviye, her iş kendi ayrıntılı kriterlerini taşır):
1. Bir kullanıcı uygulamayı açtığında cihazı `push_device_tokens`'a kayıtlı olur, ikinci açılışta
   yeni satır açılmaz (idempotent) → §1.1, §2.1.
2. Aynı cihazda farklı bir çalışan giriş yaptığında eski kullanıcının token'ı `revoked_at` alır,
   yeni kullanıcı için yeni satır açılır (çapraz-hesap push sızıntısı olmaz) → §2.1, §1.2 `Revoke`.
3. Push-etkin bir şablon (ör. `approval.task_assigned`) yayınlandığında, e-posta/in-app satırlarına
   ek olarak `channel="push"` bir `notification_deliveries` satırı açılır ve worker onu ayrı
   döngüde işler; e-posta worker'ı bu satırı **claim etmez** → §3.2, §3.4.
4. Expo `DeviceNotRegistered` döndürdüğünde ilgili token `revoked_at` alır, bir daha o token'a
   gönderim denenmez → §3.5.
5. Logout sonrası o cihazın token'ı silinir/revoke edilir (mobil taraf) → §2.2, §4.

**Riskli alan:** `src/Modules/Notifications/Sense.Modules.Notifications.Infrastructure/Persistence/Migrations/**`
(§1, migration üretimi insan işi), `apps/senseik-mobile/src/services/AuthService.ts` ve
`apps/senseik-mobile/src/store/authStore.ts` (§4, logout entegrasyonu — bu doküman değiştirmedi).
Diğer tüm dosyalar (Notifications modülünün geri kalanı, mobil `PushNotificationService.ts`,
`pushNavigation.ts`) riskli alan listesinde değil ama bu kart yalnız tasarım ürettiği için hiçbiri
değiştirilmedi.

**Açık konular:**
- Push metninde isim/rakam gösterilip gösterilmeyeceği (kilit ekranı görünürlüğü) — §3.5.
- Expo receipt ucunun (teslim onayı) ilk fazda kapsam dışı bırakılması — §3.5.
- Revoke edilmiş token'ların kalıcı silinme süresi (90 gün önerisi, kesin değil) — §5.
- DELETE ucunun path segment mi yoksa `POST .../devices/revoke` mi olacağı (token'daki `[]`
  karakterleri) — §2.2.
- `EXPO_PUBLIC_PROJECT_ID` / `app.json`'daki yer tutucu EAS project id — §4.
