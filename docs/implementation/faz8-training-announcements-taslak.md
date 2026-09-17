# Faz 8 — Training ve Announcements modülü tasarım taslağı

**Durum:** PO karar gelmeden önce, izin anahtarları ve ilk varlık modellerinin yapısı için ön tasarım.

**Bağlam:** Faz 8'in Performance modülü (Hedef + Değerlendirme dönemi) ilk sürümü tamamlandı. Training ve Announcements özelikleri bu turda tasarım aşamasında. `SystemRoleDefinitions.cs`'te joker izin desenleri zaten ayrılmıştır (`training.*`, `announcements.*`); bu belge, hangi modül yapısında ve hangi ilk varlıklarla uygulanacağını açıklar.

## Modül sınırı — seçenekler

### Seçenek A: Ayrı modüller (önerilen)
- **`Sense.Modules.Training`** — eğitim programları, katılım, sertifikalar
- **`Sense.Modules.Announcements`** — kurumsal duyurular, feed, okunma kaydı
- **Avantaj:** tenant/audit izolasyonu temiz, izin granülaritesi yüksek (`training.*` vs `announcements.*` bağımsız)
- **Dezavantaj:** iki modül altyapısı, çakışan özelliklerin köprülemesi (ör. duyuru notification tetikleme)

### Seçenek B: Performance alt-özelliği
- Training/Announcements agregatleri aynı Performance DbContext altında
- **Avantaj:** tek modül yönetimi, uzun vadıda Performance → Engagement tarafından görülen bütünlüklü tasarım
- **Dezavantaj:** Performance şeması büyür (ilk tur hedef/değerlendirme kapalı kalmıştır; schema consistency karmaşık), performans/engagement konsepti karıştığında Model çalışmaz

### Seçenek C: Ortak Communications modülü
- `Sense.Modules.Communications` → Training + Announcements + Survey (sonraki tur)
- **Avantaj:** birbirini tamamlayan özellikler (kampanya duyuru + eğitim önerisi vb.) yönetilir
- **Dezavantaj:** Survey/eNPS modül sınırı henüz açık; Communications'ın E-NPS vs Pulse Survey farkını tutması zor

**PO Kararı Beklemede:** Seçenek A (ayrı modüller) önerilir; karar sonrası ilk varlık katalogları güncellenir.

## İlk varlık modelleri (A seçeneği örneği)

### Training modülü

#### `TrainingProgram` (agregat kökü)
```
id: Guid
tenantId: Guid
title: string ≤ 200
description: string | null
category: TrainingCategory (enumeration: Technical, Compliance, Soft Skills, Custom)
startDate: DateTime
endDate: DateTime | null
maxCapacity: int | null
createdAt: DateTime
status: TrainingStatus (Draft, Active, Closed, Archived)
```

**Yanı sıra:**
- `Enrollment` (çalışan + katılım durumu + tamamlama tarihi + sertifika)
- `TrainingSchedule` (seans tarihleri — isteğe bağlı, ilk sürümde basit)

**Veri korunması:**
- Tamamlanan eğitim kaydı `Archived` duruma alınabiliyor; silme yok (denetim izi)

#### İzin anahtarları
- `training.programs.view` — mevcut/arşiv eğitim programlarını görüntüleme
- `training.programs.manage` — program oluşturma, düzenleme, kapatma
- `training.enrollments.view` — katılım listesi görüntüleme
- `training.enrollments.manage` — katılımcı ekleme/çıkarma, sertifika düzenleme
- `training.my.enrollments` — çalışan kendi katılımlarını görüntüleme (SelfService joker)
- `training.my.completion` — kendi tamamlama ilerleme gözlemlemesi

**İz:** HR/Manager/Trainee'ler → `training.*`, Employee → `training.my.*`, Intern → `training.my.enrollments` (sadece okunur)

### Announcements modülü

#### `Announcement` (agregat kökü)
```
id: Guid
tenantId: Guid
title: string ≤ 200
content: string
type: AnnouncementType (General, HR, Policy, Emergency)
priority: Priority (Low, Normal, High, Urgent)
publishedAt: DateTime
publishedBy: Guid (Employee)
status: AnnouncementStatus (Draft, Published, Expired, Archived)
expiresAt: DateTime | null
targetAudience: TargetAudience (Everyone, Managers, HR, Employees, Custom Roles)
```

**Yanı sıra:**
- `AnnouncementReadRecord` — kim, ne zaman okudu (analytics)
- `Attachment` (isteğe bağlı dosya eki) — önceki tur İntegrasyon modülünün `WebhookSubscription` emsali

#### İzin anahtarları
- `announcements.manage` — duyuru yayınlama, düzenleme, silme
- `announcements.feed.view` — kendi feed'ini görüntüleme
- `announcements.read_receipts` — okunma kayıtlarını analytics için sorgulamak
- `announcements.my.feed` — SelfService version, Employee/Intern/Contractor

**İz:**
- HR/Manager: `announcements.manage`, `announcements.read_receipts`
- Employee/Intern/Contractor: `announcements.my.feed`
- Auditor: `announcements.manage` (read-only), `announcements.read_receipts`

## İzin deseni summary

Mevcut `SystemRoleDefinitions.cs` (satır 64, 71, 87, 135, 140, 144):

```
HR:           "training.*", "announcements.*"
HRSpecialist: "training.*", "announcements.*"
Manager:      "training.*.view"
Employee:     "announcements.feed.*" (wildcard altında)
Intern:       "announcements.feed.*"
Contractor:   "announcements.feed.*"
```

**Yeni anahtarlar yukarıdaki tabloyla tutarlıdır** — roll-out sırasında otomatik eşlenir.

## Entegrasyon noktaları

1. **Notification:** Training başlangıcı → Notifications.Contracts `INotificationService.SendAsync()`
2. **Workflow:** Eğitim tamamlama onayı (İK) → Workflow.Contracts `IApprovalRequest`
3. **Analytics:** Duyuru okunma, eğitim tamamlama → Analytics.Contracts (IAnnouncementAnalyticsClient vb.)
4. **Employee Termination:** Çıkan çalışan eğitimlerini Archived; açık Announcements feed'den çıkar (geçmiş başladı/bitti olanlar kalır)

## Açık sorular (PO'ya)

1. **Modül sınırı:** A, B veya C seçeneği onaylanıyor mu?
2. **Training sertifikası:** Dış sistem ile SCORM/xAPI senkronizasyonu gerekir mi? (ilk sürümde manuel başlangıç önerilir)
3. **Announcements mecrası:** E-mail, SMS, push — hangileri açılacak? (ilk sürümde in-app feed önerilir; Notifications.Contracts üzerinden genişletilir)
4. **Denetim izi detayı:** Duyuru düzenleme tarihçesi full log mu, state snapshot mı?

---

**Sonraki adım:** PO seçiminden sonra ilk varlık/repository/service yapı kodlanır; tur 2 veya 3'te API uçları açılır.
