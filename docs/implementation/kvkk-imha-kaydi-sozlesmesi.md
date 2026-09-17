# KVKK Veri İmha Kaydı — Tasarım Belgesi

## Amaç

KVKK (Kanun Numarası 6698) Madde 7 (Veri imha ve yok etme) ve Kişisel Verilerin Silinmesi, Yok Edilmesi veya Anonim Hale Getirilmesi Hakkında Tebliğ (Tebliğ Numarası 2020/13) kapsamında, şirketlerin veri imha işlemlerini belgelendirmesi gerekir. Mevcut durum:

- `CandidateRetentionSweepJobHandler` (Recruitment): aday verisi imha ettiklerinde **yalnız** `ILogger.LogInformation` ile yazıyor
- `DocumentExpiryReminderJobHandler` (Documents): belge süresi hatırlatması olay olarak yayınlıyor, imha deği

Sorun: uygulama logları rotate olunca ("kim ne zaman kaç kayıt imha etti" kanıtı) sorunsuz kaybol ediyor.

**Çözüm:** kalıcı `kvkk_deletion_logs` tablosu; hangi job'lar hangi zamanlar kaç kayıt imha ettiği kesin kaydı.

---

## Önerilen Tablo Şeması

```sql
CREATE TABLE kvkk_deletion_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    
    -- Kim (sistem tarafı)
    job_type VARCHAR(50) NOT NULL,  -- e.g., "CandidateRetentionSweep", "DocumentExpiry"
    triggered_by_job_id VARCHAR(256) NOT NULL,  -- Hangfire Job ID (başvuru için)
    
    -- Ne
    entity_type VARCHAR(100) NOT NULL,  -- e.g., "Candidate", "Document"
    deletion_count INT NOT NULL,  -- kaç kayıt imha edildi
    details JSONB,  -- işe-özgü ek bilgi (optional, ör. "candidate_ids" ilk 100'ü, çalışan filtresi vs.)
    
    -- Ne zaman
    executed_at TIMESTAMPTZ NOT NULL,  -- trigger zamanı (context.TriggeredAt)
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),  -- DB insert zamanı (ispat)
    
    -- Durum
    status VARCHAR(20) NOT NULL DEFAULT 'success',  -- 'success' | 'partial' | 'error'
    error_message VARCHAR(2000),  -- hata varsa açıklama
    
    -- Yönetim
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_kvkk_deletion_logs_tenant_executed
    ON kvkk_deletion_logs (tenant_id, executed_at DESC);

CREATE INDEX idx_kvkk_deletion_logs_job_type
    ON kvkk_deletion_logs (tenant_id, job_type, executed_at DESC);
```

---

## Hangi Job'lar Yazacak?

### Mevcut

1. **`CandidateRetentionSweepJobHandler`** (Faz 9 / Recruitment)
   - **Ne:** Aday kayıtları ve form cevapları (KVKK saklama süresi dolan)
   - **Yazacak:** Her koşuda `anonymized_count`'ı kaydeder
   - **Zaman:** Günlük 03:30 UTC (Europe/Istanbul intent)

2. **Ek: `DocumentExpiryReminderJobHandler`** (Faz 4 / Documents)
   - **Durum:** Şu an **sadece hatırlatma** (log değil, olay)
   - **Karar:** İlk sürümde *optional* — belge süresi sonunda otomatik silme değil, kullanıcı aksiyonu. İleri aşamalarda otomatik silme handler'ı eklenir ise, bu tabloyu yazacaktır.

### İleri Dönem (Faz 10+)

- Başka KVKK delete işlemi çıkarsa aynı tabloyu kullanır.

---

## Saklama Süresi

| Kayıt Türü | Saklama | Gerekçe |
|---|---|---|
| Başarılı imha | **3 yıl** | KVKK md.11 (denetim + rıza/işlem kanıtı); İmha Yönetmeliği (2020/13) yazılı kayıt |
| Hata/kısmi | **2 yıl** | Muhasebe müdürlüğü log + soruşturma arşivi |
| Otomatik silme | **T+90 gün** | Tebliğ Ek-2: "imha iş yapılırsa 3 ay sonra tebliğ edilir" → log 3 ay *en az* tutulmalı |

**İmplementasyon:** Migration eklenince, saklama süresi geçmiş kayıtlar otomatik silinecek şekilde job/policy yazılması PO kararıdır (Bu belge kapsamı dışı).

---

## Veri Akışı

```
CandidateRetentionSweepJobHandler.ExecuteAsync()
  ├─ expired = await candidates.ListRetentionExpiredAsync(cutoff)
  ├─ foreach candidate:
  │    ├─ candidate.Anonymize()
  │    ├─ ScrubAsync(formResponses) — imha
  │    └─ anonymizedCount++
  ├─ SaveChangesAsync()
  ├─ [TÜRMELİ] kvkk_deletion_logs.Insert({
  │       tenant_id, job_type: "CandidateRetentionSweep",
  │       entity_type: "Candidate", deletion_count: anonymizedCount,
  │       executed_at: context.TriggeredAt, status: "success"
  │    })
  └─ logger.LogInformation("...") — ilk sürümde çift yazı olur (log + DB)
```

---

## PO'nun Karar Vermesi Gereken Noktalar

1. **Tablo şeması nerede durur?**
   - [ ] `identity` şeması (sistem-geneli, birden fazla modül)
   - [ ] Yeni `kvkk` şeması (ayrık, izin/audit izole)
   - [ ] `shared` (Shared.Infrastructure tarafında; başka modüller bağımlılık alır)
   
   **Önerilen:** `identity` şeması (İmza/Audit benzer sistemi, izinler orada tanımlı)

2. **Log kaydı nasıl yazılır?**
   - [ ] Handler içinde doğrudan repository çağrısı (gerçek imha işi ile aynı transaction'da)
   - [ ] Integration event via outbox (imha → event → outbox → Notifications/genel log consumer)
   
   **Önerilen:** Handler içinde doğrudan (transaction'ı kaybetmeyecek şekilde, SaveChanges'ın *aynı çağrısında* — imha başladı, kaydı başladı, her ikisi de kaydetmeyi başarırsa kaydedilir)

3. **İleri aşama:**
   - Bir "KVKK İmha Raporu" sayfası İK/Uyum üniteleri için mi (ekran)?
   - Rapor `tenant_id` bazlı mı, sistem-geneli mi?

4. **Hata süreci:**
   - `CandidateRetentionSweepJobHandler` hata attıktan sonra otomatik tekrar denemesi mi (BackgroundJobSeedSynchronizer'ın try/catch mekanizması ile)?
   - Başarısız imha kaydı status='error' ile tablo'ya yazılır mı?

---

## Ek: Hassas Alan Güvenliği

- Tablo genel veritabanında olsa da, hangi kullanıcı `kvkk_deletion_logs` tablosunu okuyabilir **yalnız** `identity.audit.sensitive_view` (veya benzeri) izni olanlar olmalı.
- `entity_type` ve `deletion_count` herkese açık; ama `details` (imha kaydının özeti) **yalnız İK + Uyum**.
- Veri sahibi (veri tarafı) **log tarafını hiç görmemeli** (audit izne tabi).

---

## Referans

- KVKK Madde 7: https://www.mevzuat.gov.tr/MevzuatMetin/1.5.6698.pdf (Madde 7)
- Tebliğ 2020/13: İmha notu, saklama, belge gereksinimleri

---

## Sonraki Adımlar

1. **Migration üretimi:** ADR-0005 (PO aksiyonu). Tablo şeması PO'nun kararı sonra fixture edilir.
2. **Handler implementasyonu:** `CandidateRetentionSweepJobHandler` tarafından kullanılması Faz 9'e sonraki tur verilir (yapı bu turda yazıldı, implementasyon tur 2 adayı).
3. **Raporlama/UI:** Faz 10 veya sonrası — "KVKK İmha Özetim" sayfası.

