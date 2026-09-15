# Faz 1a Durum Takibi (PC-A: Employee / özlük)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 1 satırları, ayrıntı [11.3.03-ozluk.md](./11.3.03-ozluk.md); iş bölümü [paralel-gelistirme-plani.md](../../implementation/paralel-gelistirme-plani.md). Faz 1'in Workflow / Notifications / Leave kalemleri (#14–#31, #42–#44) PC-B hattındadır ve burada izlenmez.

**Son güncelleme:** 2026-09-15
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti · ⏸ Bağımlılık bekliyor.

## PC-B için senkronizasyon noktaları

| Kod | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| S1 | Birime onay süreci ataması slotu (talep türü başına politika, üst birimden devralma, çözümleme kontratı) — #29'un Organization tarafı | ✅ Bitti | `IOrgApprovalPolicyResolver` (Organization.Contracts, unit + aktif atalar, ilk eşleşme kazanır), `ApprovalRequestTypes{Leave,Advance,Expense,Overtime}`, `GET/PUT /organization/units/{id}/approval-policies`, entegrasyon olayı `OrgUnitApprovalPolicyChanged`; web: birim düzenleme diyaloğunda "Onay süreçleri" bölümü (kendi/miras değeri gösterir; Workflow henüz yoksa seçiciler devre dışı + bilgi notu) | Politika kataloğu (seçenek listesi) Workflow'dan gelecek (PC-B); `ApprovalPolicyId` bugün doğrulanmıyor — Workflow, silinmiş politika id'lerine tolerans göstermeli |
| S2 | Çalışan kartı sekme slotları (İzinler, Fazla mesailer, Ekstra izinler) | ✅ Bitti | Rota `/app/employees/:id/:tab?`, sekme kayıt defteri `web/src/components/employees/card-tabs.tsx` (key/labelKey/permission/accessSetting/phase/component — tek satırlık ekleme notu); Genel/Kariyer/Hesap dolu, Kişisel/Ödemeler/Diğer boş durum, İzinler/Fazla mesailer "Faz 1b" rozetiyle | İçerik (İzinler, Fazla mesailer) Leave'den gelecek (PC-B); Kişisel/Ödemeler/Diğer içeriği E3/E4/sonraki fazlarda |

**Önemli bulgu (E0 sırasında tespit edildi, kapsamı Faz 1a'yı aşıyor):** Agregatların `Raise(...)` ile ürettiği domain event'ler (`UserCreated`, `EmployeeCreated`, `OrgUnitCreated` vb.) hiçbir yerde okunmuyor/kaydedilmiyor — outbox'a yalnızca elle `IIntegrationEventOutbox` çağrılan entegrasyon olayları düşüyor (bugüne kadar tek örnek: S1'in `OrgUnitApprovalPolicyChanged`'i). Ayrıca `Sense.Worker` yalnızca Identity'nin outbox'ını dinliyordu; Organization'ın outbox'ı hiç işlenmiyordu. Kullanıcı kararıyla dar kapsamlı düzeltme yapıldı: `Sense.Worker/Program.cs`'e Organization için de outbox dinleyicisi ve `EventTypeRegistry` kaydı eklendi (izole ortamda gerçek Worker sürecinde uçtan uca doğrulandı — bkz. değişiklik geçmişi). Identity/Employee/Documents/DataTransfer için henüz outbox'a yazan bir çağrı yok, bu yüzden dokunulmadı. **Genel çözüm** (agregat event'lerinin SaveChanges sırasında otomatik outbox'a yazılması + Worker'ın tüm modülleri yüklemesi) PC-B ile koordine edilerek ayrı bir görev olarak ele alınmalı; Workflow modülü bu boru hattına dayanacağı için özellikle önemli.

## Faz 1a kalemleri

| # | Kalem | Paket | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|---|
| 32 | Özlük alan/seçenek listeleri (medeni hal, eğitim, sözleşme türü, askerlik, engel, kan grubu, maaş bloğu, banka hesap tipi, pozisyon başlama tarihi, çalışma şekli, birim ve özel alan kolonları) + özel alanlar raporlanabilir + kullanıcı tanımlı ayrılma nedeni | E1, E3, E4, E5a, E6 | 🟡 Kısmi | E1: `Gender`, `MaritalStatus`, `ChildCount`, `EducationStatus`, `HighestEducationLevel`, `MilitaryStatus`, `DisabilityDegree`, `BloodType`, cep/ev telefonu (`PhoneNormalizer`), adres/şehir/posta kodu, `ContractType`, `EmploymentType` (Employee domain + API + kart); hassas alanlar `employee.sensitive.view` ile (TCKN maskeli, askerlik/engel/kan grubu gizli) | Maaş bloğu ve banka (E3), pozisyon başlama tarihi (E2), özel alanlar (E4), ayrılma nedeni (E5a), aktarım şablonu kolonları (E6); TCKN'nin veritabanında şifrelenmesi yapılmadı (aktarım eşleştirmesi için kör indeks gerekiyor) |
| 33 | Çalışan kartı bölümleri (Genel, Kariyer, Kişisel, İzinler barı, Ödemeler, Fazla mesailer, Borç barı, Diğer); Ad+Soyad ile kart; iş/kişisel e-posta, iş telefonu | E0, E1, E2 | 🟡 Kısmi | Sekmeli kart (`/app/employees/:id/:tab`, `card-tabs.tsx`): Genel (+ sözleşme türü, çalışma şekli), Kişisel bilgiler (E1), Kariyer, Hesap dolu; İzinler/Fazla mesailer "Faz 1b" (PC-B); Ad+Soyad ile hızlı ekleme → `Draft` | Ödemeler (E3), Diğer (sonraki fazlar), kariyer geçmişi (E2) |
| 34 | `ContractType` (belirsiz/belirli süreli + alt tür) + `EmploymentType` genişletilmiş değerler | E1 | ✅ Bitti | `ContractType{Indefinite, FixedTerm}`, `EmploymentType{FullTime, PartTime, Temporary, Contract, Freelance, Intern, Mobile, Apprentice, Seasonal, AgencyWorker, Subcontractor}` (domain, API, kart Genel sekmesi) | Alt tür (`ContractSubType`: çağrı üzerine, takım sözleşmesi) spesifikasyonda [Aday] — eklenmedi |
| 35 | Sendika bilgileri + çalışan durumu **Askıda** | E1 (durum), E5b (sendika) | 🟡 Kısmi | Durum modeli `EmployeeStatus{Draft, Active, Suspended, Terminated}` (eski Active/OnLeave→Active, Inactive→Terminated veri göçü), `SuspensionReason` (askerlik, sendika yöneticiliği, uzun ücretsiz izin, gözaltı, sağlık raporu, zorlayıcı neden, diğer), `POST /employees/{id}/suspend|reactivate`, entegrasyon olayları `EmployeeSuspended`/`EmployeeReactivated` | Askıya alma/geri alma için web düğmesi yok; sendika bilgileri E5b |
| 36 | Birlikte istihdam / grup içi transfer kıdem devamı, işyeri devri, rehire/fasılalı dönem birleştirme, `CareerHistory.ChangeType` | E2, E5a, E5b | ❌ Başlanmadı | — | Hesap tarafı Faz 3 |
| 37 | Alt işveren (taşeron) firma kaydı + çalışan ilişkisi | E5b | ❌ Başlanmadı | — | İSG 50 çalışan uyarısı Faz 7 |
| 38 | İşten ayrılış sihirbazı: fesih türü + iş güvencesi kapsam uyarısı | E5a | ❌ Başlanmadı | — | Eşikler `LegislationParameter` ADR'ı (PC-B) gelene kadar yapılandırmadan okunur |
| 39 | Toplu işten çıkarma eşik uyarısı (30 gün penceresi) | E5a | ❌ Başlanmadı | — | Analitik tarafı Faz 7 |
| 40 | Self-servis: kendi bilgilerini güncelleme, bazı özel alanları çalışanın düzenlemesi | E4 | ❌ Başlanmadı | — | Onaylı değişiklik talebi Workflow'a (PC-B) bağlanacak |
| 41 | Ana sayfa widget'ları | E7 | 🟡 Kısmi | Faz 0: Resmi tatiller, Yaklaşan doğum günleri | Birim/ekip bilgisi (PC-A); onay bekleyen talepler, yaklaşan izinler PC-B; etkinlik takvimi Faz 4 |
| 45 | Yazılı sözleşme / çalışma koşulları belgesi 2 ay kuralı: son tarih + hatırlatıcı | E5b | ❌ Başlanmadı | — | Belge şablonu Faz 4 |
| — | Özlük aktarım şablonunun yeni alanlarla genişletilmesi (seçenek listeleri, maaş bloğu grup kuralı, özel alan kolonları) | E6 | ❌ Başlanmadı | — | |
| — | Çalışanlar grid'i: durum/çalışma şekli/sözleşme türü/birim seviyesi filtreleri, "Eksik bilgi" rozeti | E7 | ❌ Başlanmadı | — | |

## Paket sırası

E0 (S1, S2) → E1 (kimlik/kişisel/istihdam alanları, durum modeli, kart bölümleri) → E2 (kariyer geçmişi, seviye bazlı birim ataması) → E3 (maaş + banka) → E4 (özel alanlar + self-servis) → E5a (işten ayrılış, iş güvencesi, toplu çıkarma, çalışma dönemleri, yeniden işe alım) → E5b (sendika, alt işveren, sözleşme belgesi hatırlatıcısı, grup içi transfer/işyeri devri alanları) → E6 (aktarım şablonu) → E7 (grid ve ana sayfa).

## Değişiklik geçmişi

- 2026-09-15: Dosya oluşturuldu; PC-A hattı başlatıldı.
- 2026-09-15: E0 tamamlandı (S1, S2). Ek olarak `Sense.Worker`'a Organization outbox dinleyicisi eklendi; izole Docker ortamında sahte bir `OrgUnitApprovalPolicyChanged` satırı outbox'a yazılıp gerçek Worker süreciyle işlendi, `processed_at` doldu ve hata oluşmadı. Backend derleniyor, tüm test projeleri (Organization 65, Identity 45 dahil) geçiyor. Web `tsc`/`eslint`/`vite build` temiz.
- 2026-09-15: E1 tamamlandı: kişisel/istihdam alanları, durum modeli (Draft/Active/Suspended/Terminated), `EmployeeHired`/`EmployeeSuspended`/`EmployeeReactivated` entegrasyon olayları (Employee.Contracts), `employee.sensitive.view` maskelemesi, kartta Kişisel bilgiler sekmesi; `IEncryptionProvider` Shared'a taşındı. Worker'a Employee outbox dinleyicisi eklendi ve gerçek Worker sürecinde `EmployeeHired` satırı işlendi. Açık kalanlar: TCKN'nin veritabanında şifrelenmesi (kör indeks + aktarım eşleştirmesi değişikliği gerekiyor), askıya alma web düğmesi, aktarımla işe alınan çalışanlarda `EmployeeHired` yayınlanmıyor (E6'da ele alınacak).
