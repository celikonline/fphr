# Faz 1a Durum Takibi (PC-A: Employee / özlük)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 1 satırları, ayrıntı [11.3.03-ozluk.md](./11.3.03-ozluk.md); iş bölümü [paralel-gelistirme-plani.md](../../implementation/paralel-gelistirme-plani.md). Faz 1'in Workflow / Notifications / Leave kalemleri (#14–#31, #42–#44) PC-B hattındadır ve burada izlenmez.

**Son güncelleme:** 2026-09-15
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti · ⏸ Bağımlılık bekliyor.

## PC-B için senkronizasyon noktaları

| Kod | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| S1 | Birime onay süreci ataması slotu (talep türü başına politika, üst birimden devralma, çözümleme kontratı) — #29'un Organization tarafı | ❌ Başlanmadı | — | Politika kataloğu Workflow'dan gelecek (PC-B) |
| S2 | Çalışan kartı sekme slotları (İzinler, Fazla mesailer, Ekstra izinler) | ❌ Başlanmadı | — | İçerik Leave'den gelecek (PC-B) |

## Faz 1a kalemleri

| # | Kalem | Paket | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|---|
| 32 | Özlük alan/seçenek listeleri (medeni hal, eğitim, sözleşme türü, askerlik, engel, kan grubu, maaş bloğu, banka hesap tipi, pozisyon başlama tarihi, çalışma şekli, birim ve özel alan kolonları) + özel alanlar raporlanabilir + kullanıcı tanımlı ayrılma nedeni | E1, E3, E4, E5a, E6 | ❌ Başlanmadı | — | |
| 33 | Çalışan kartı bölümleri (Genel, Kariyer, Kişisel, İzinler barı, Ödemeler, Fazla mesailer, Borç barı, Diğer); Ad+Soyad ile kart; iş/kişisel e-posta, iş telefonu | E1, E2 | 🟡 Kısmi | Faz 0: Genel/Kariyer/Hesap bölümleri, Ad+Soyad ile kart, iş/kişisel e-posta, iş telefonu | Kişisel bilgiler, Ödemeler, Diğer bölümleri ve sekme yapısı |
| 34 | `ContractType` (belirsiz/belirli süreli + alt tür) + `EmploymentType` genişletilmiş değerler | E1 | ❌ Başlanmadı | — | |
| 35 | Sendika bilgileri + çalışan durumu **Askıda** | E1 (durum), E5b (sendika) | ❌ Başlanmadı | — | |
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
