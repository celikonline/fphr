# Faz 3 — Owner Hesap Devri (Transfer Ownership) Tasarım Taslağı

**Durum:** Tur 10 tespit işi (tasarım taslağı, kod yok). Yakında yapılandırılacak.

## Sorun

Bir Owner kullanıcı çalışma hayatını bitirdiğinde (fesih), kiracının tek Owner'ı mı kaldığı kontrol edilir:
- **Evet (tek Owner):** Sistem onu disable etmez, erişim kapalı kalmaz (kiracı kilitlenmez) — `EmployeeTerminatedHandler` içinde.
- **Hayır (birden fazla Owner):** Onu disable ediliyor.

Ancak "tek aktif Owner'ı disable edemiyorum" durumu sonsuza kadar kalmaz. Owner hesap devri (transfer ownership) mekanizması, eski Owner'ın çıkışını temiz hale getirmeli.

## Önerilen Tasarım

### 1. Kapsamı
- **Scope:** Yalnızca Owner rolü sahibi kullanıcılar. Diğer roller (Admin, HR vb.) birden fazla olabilse de devir mekanizması yoktur — ilk sürümün kapsı dışı.
- **Tetikleyici:** El ile (İK yöneticisinin iş akışı / İK paneli) ya da otomatik (EmployeeTerminated olayı).

### 2. Komut İmzası (taslak)

```csharp
public sealed record TransferOwnershipCommand(
    Guid TenantId,
    Guid CurrentOwnerId,      // Eski Owner (devre dışı bırakılacak kullanıcı)
    Guid NewOwnerId,          // Yeni Owner (devir alacak kullanıcı)
    string? Reason = null     // İsteğe bağlı, audit için
) : ICommand;
```

### 3. Kurallar ve Koruması

**Ön Koşullar:**
- `CurrentOwner` kullanıcısı Active ve Owner rolüne sahip
- `NewOwner` kullanıcısı Active
- Yalnızca **tek bir Owner** (CurrentOwner) Active olmalı (devrim öncesinde)

**İş Mantığı:**
1. NewOwner'a Owner rolü ekle (zaten varsa idempotent)
2. CurrentOwner'ı disable et
3. Audit log kaydı: "Owner X → Y'ye devredildi"

**Hata Durumları:**
- `identity.ownership.transfer_forbidden_multiple_owners`: Zaten birden fazla Owner varsa transfer yapma (kural ihlali)
- `identity.ownership.target_not_found`: NewOwner kullanıcı yok
- `identity.ownership.target_not_active`: NewOwner pasif
- `identity.ownership.source_not_owner`: CurrentOwner Owner rolüne sahip değil

### 4. Endpoint (taslak)

```
POST /api/v1/identity/admin/ownership/transfer
Body: { currentOwnerId, newOwnerId, reason? }
Response: 204 No Content
Perms: identity.admin.manage (yalnızca admin)
```

### 5. Bağlantı: EmployeeTerminated Olayı

Otomatik devir için, `EmployeeTerminatedHandler` şu mantıkla genişletilebilir:

```csharp
// Fesih yapılan kullanıcı Owner ve tek Owner'ıysa:
if (user.HasRole(ownerRole.Id) && activeOwnerCount == 1) {
    // Devir işlemi başlat (hangi yeni Owner'a? İK panelinden seçilmeli — otomatik değil)
    // Şimdilik: hata at, "İK yöneticisi el ile atama yapmalı" mesajı
}
```

### 6. Referans: Tek Owner Koruması Kalıbı

`EmployeeTerminatedHandler`'deki mevcut koruma (satır 35-43):
```csharp
var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, cancellationToken);
if (ownerRole is not null && user.HasRole(ownerRole.Id)
    && await users.CountActiveOwnersAsync(cancellationToken) <= 1)
{
    logger.LogWarning("Kiracının tek aktif Owner'ı, devre dışı bırakılmadı...");
    return;
}
```

Owner transfer tasarımı bu **sama "tek Owner" kontrolünü** kullanmalı.

## Sonraki Adımlar

1. **Faz 3 veya 4:** Gereksinim teyidi (otomatik mi, el mi, her ikisi mi?)
2. **Kod Yazımı:** `Identity.Application/Users/TransferOwnershipHandler.cs` + test
3. **Endpoint:** `MeController` veya admin controller'ında `TransferOwnership` ucu
4. **Audit:** `TransferOwnership` eyleminin audit log'lanması

---

**Not:** Bu taslak, Faz 3 Tur 10'de yapılmıştır ve Faz 3'ün Tur 1 "Owner transfer" işine dayanak oluşturmaktadır.
