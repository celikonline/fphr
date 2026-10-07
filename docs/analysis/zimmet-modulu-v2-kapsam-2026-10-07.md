# Zimmet / Varlık Yönetimi v2 — İK değerlendirmesi, boşluk analizi ve sözleşme (2026-10-07)

Kaynak: mevcut `Assets` modülü (kod incelemesi) + `docs/analysis/kolayik-video/11.3.08-*.md` + İK pratiği
(zimmet tutanağı, teslim onayı, iade/kayıp süreçleri, çıkış kontrol listesi). Kararlar kullanıcı adına
otonom alınmıştır (kullanıcı çevrimdışı); her karar gerekçesiyle aşağıda.

## 1. Mevcut durum (v1, değişmeden korunur)

Kategori kataloğu (KKD yenileme aralığı dahil), varlık CRUD, **Workflow onaylı atama** (`PendingAssignment`),
iade, hizmet dışı, zimmet tutanağı PDF'i (Documents'a düşer), Excel içe aktarma, işten çıkışta iade edilmemiş
zimmet denetimi, çalışan kartı sekmesi ve PDF bölümü, "Zimmetlerim" ekranı, bildirimler.

## 2. Bir İK uzmanı gözüyle boşluklar

| # | Boşluk | Neden önemli | Karar |
|---|---|---|---|
| G1 | **Atama geçmişi yok** (tek "mevcut atama" yuvası; iade edince kim/ne zaman bilgisi kayboluyor) | Anlaşmazlıkta (hasar, kayıp, işten çıkış) "bu cihaz kimdeydi" sorusu cevapsız; denetim izi eksik | **YAPILIR** — `AssetAssignment` defteri (aggregate'in alt varlığı), mevcut açık atamalar migration'da geriye dönük doldurulur |
| G2 | **Teslim onayı yok** | Zimmet tutanağı çalışanın "teslim aldım" beyanı olmadan hukuken zayıf | **YAPILIR** — çalışan kendi zimmetini "teslim aldım" ile onaylar (`acknowledge`), tarih defterde tutulur |
| G3 | **Varlık künyesi zayıf**: marka, model, demirbaş no, alış tarihi/bedeli, garanti bitişi, durum (sağlam/hasarlı) yok | Envanter sayımı, garanti takibi, hasar tazmini | **YAPILIR** — alanlar eklenir; demirbaş no kiracı içinde tekil |
| G4 | **İade durumu/notu yok** | Hasarlı iade kaydı olmadan tazmin/yenileme kararı verilemez | **YAPILIR** — iade sırasında durum (sağlam/orta/hasarlı) + not; varlığın güncel durumu da güncellenir |
| G5 | **Kayıp durumu yok** (yalnız "hizmet dışı") | Kayıp ile emekliye ayırma farklı süreçler; kayıp zimmet açık kalamaz | **YAPILIR** — `Lost` durumu; zimmetliyse atama "kayıp" nedeniyle kapanır |
| G6 | **Özet/pano yok**; arama yok | İK günlük "kaç zimmet boşta, kaç onaylanmamış, garantisi biten, KKD yenilemesi geciken" sorusunu soruyor | **YAPILIR** — özet ucu + envanterde arama |
| G7 | Çalışan tarafında geçmiş yok | Çalışan "geçmişte hangi zimmetler bendeydi" görmek ister | **YAPILIR** — "Zimmetlerim"de ve kartta geçmiş |
| G8 | Web `AssetStatus` tipi `pendingAssignment`'ı bilmiyor (v1 hatası) | Onay bekleyen zimmet UI'da bozuk/etiketsiz görünür | **YAPILIR** (v2 ile düzeltilir) |

### Bilinçli olarak ERTELENENLER (gerekçe)
- **Amortisman / muhasebe değeri**: muhasebe sistemi işi; yalnız alış bedeli tutulur.
- **Bakım/onarım takibi, barkod/QR, sarf stok (tüketim) yönetimi**: ayrı faz; ihtiyaç doğrulanmadan şişirilmez (YAGNI).
- **Zimmet kaybında maaştan kesinti**: hukuki (İş K. m.32) + bordro kararı gerektirir; insan kararı. Kayıp kaydı ve not defterde, kesinti süreci yok.
- **Excel şablonuna yeni kolonlar**: `AssetImportRowHandler` riskli alan; v1 şablonu aynen kalır (geçmiş defteri yine de otomatik oluşur, çünkü domain metotları defteri yazar).
- **Mobil uygulama ekranı**: web önceliği; API mobil için hazır.

## 3. Alan modeli (karar)

`Asset` (aggregate, mevcut) + yeni alanlar:
`AssetTag?` (demirbaş no, ≤50, kiracı içinde büyük/küçük harf duyarsız tekil), `Brand?` (≤100), `Model?` (≤100),
`PurchaseDate?`, `PurchaseCost?` (decimal(18,2), ≥0, para birimi kiracı varsayılanı — alan eklenmez), `WarrantyEndsOn?`
(doluysa ≥ PurchaseDate), `Condition` (`Good|Fair|Damaged`, varsayılan `Good`).
`AssetStatus`'a **`Lost = 4`** eklenir (mevcut: Unassigned=0, Assigned=1, Retired=2, PendingAssignment=3).

`AssetAssignment` (aggregate'in alt varlığı, tablo `assets.asset_assignments`, TenantId taşır):
`Id, AssetId, EmployeeId, IssuedAt, ReturnedAt?, EndReason? (Returned|Lost), ReturnCondition? (Good|Fair|Damaged),
Note?, AcknowledgedAt?`. Kural: bir varlığın aynı anda **en çok bir açık** (`ReturnedAt == null`) ataması olur.
Defter, domain metotlarıyla yazılır (`Assign`, `CompleteAssignment` açar; `Return`, `MarkLost` kapatır) — bu sayede
Excel içe aktarma (`Assign`/`Return` çağırır) koda dokunmadan defteri doldurur.
Repository, değiştirilecek varlıkları **yalnız açık atamayı Include ederek** yükler
(`Include(a => a.Assignments.Where(x => x.ReturnedAt == null))`); geçmiş sorguları ayrı okunur.

Yeni domain hata kodları (resx TR+EN): `asset.tag_in_use` (Conflict), `asset.cost_invalid` (Validation),
`asset.warranty_before_purchase` (Validation), `asset.already_acknowledged` (Rule), `asset.not_assignee` (Forbidden),
`asset.cannot_lose` (Rule — Retired/Lost/PendingAssignment varlık kayıp işaretlenemez), `asset.note_too_long` (Validation).

## 4. API sözleşmesi (`/api/v1/assets`, camelCase, enum'lar camelCase string)

`AssetDto` (mevcut alanlar + yeni):
`id, categoryId, name, serialNumber, description, status (unassigned|assigned|retired|pendingAssignment|lost),
assignedEmployeeId, issuedAt, returnedAt, replacementDueOn, assetTag, brand, model, purchaseDate, purchaseCost,
warrantyEndsOn, condition (good|fair|damaged), acknowledgedAt` (`acknowledgedAt`: yalnız `assigned` iken açık atamanın onay zamanı; yoksa omitted).

| Uç | İzin | Gövde / yanıt |
|---|---|---|
| `GET /` | `assets.assets.view` | mevcut filtreler + **`search`** (ad/seri/demirbaş no/marka/model içerir, büyük-küçük harf duyarsız) |
| `POST /` | `assets.assets.manage` | `{categoryId, name, serialNumber, description, assetTag, brand, model, purchaseDate, purchaseCost, warrantyEndsOn}` → `Guid` |
| `PUT /{id}` | `assets.assets.manage` | `{name, serialNumber, description, assetTag, brand, model, purchaseDate, purchaseCost, warrantyEndsOn, condition}` → 204 |
| `POST /{id}/return` | `assets.assets.manage` | `{returnedAt, condition?, note?}` (condition varsayılan `good`; varlığın `Condition`'ı da güncellenir; not ≤500) |
| `POST /{id}/lost` | `assets.assets.manage` | `{occurredOn, note?}` — Unassigned veya Assigned'dan `Lost`'a; Assigned ise açık atama `endReason=lost` ile kapanır |
| `POST /{id}/acknowledge` | `assets.my.assets` | gövdesiz; çağıran = atanan çalışan, varlık `assigned` ve açık atama onaysız olmalı → 204 |
| `GET /{id}/history` | `assets.assets.view` | `AssetAssignmentDto[]` (yeniden eskiye) |
| `GET /employees/{employeeId}/history` | `assets.assets.view` | `EmployeeAssetHistoryDto[]` (yeniden eskiye; kapalı + açık atamalar) |
| `GET /my/history` | `assets.my.assets` | `EmployeeAssetHistoryDto[]` (çağıranın çalışan kaydı) |
| `GET /summary` | `assets.assets.view` | `AssetSummaryDto` |

`AssetAssignmentDto`: `{id, employeeId, issuedAt, returnedAt, endReason, returnCondition, note, acknowledgedAt}`.
`EmployeeAssetHistoryDto`: `AssetAssignmentDto` + `{assetId, assetName, categoryId, serialNumber, assetTag}`.
`AssetSummaryDto`: `{total, unassigned, assigned, pendingAssignment, retired, lost, unacknowledged (assigned & onaysız),
warrantyExpiring (garanti bitişi bugünden itibaren 60 gün içinde, Retired/Lost hariç), warrantyExpired (bitiş < bugün, Retired/Lost hariç),
replacementOverdue (KKD yenilemesi gecikmiş atanmış), categories: [{categoryId, total, assigned}]}` (bugün = `TimeProvider`).

Yeni izin anahtarı **yok** (mevcut anahtarlar yeterli → `SystemRoleDefinitions` değişmez).

**Backend uygulama notları (sözleşmeyi değiştirmez, davranışı netleştirir):**
- `PUT /{id}`: künye alanları (`assetTag, brand, model, purchaseDate, purchaseCost, warrantyEndsOn`) PUT anlamındadır — gönderilmeyen/`null` alan temizlenir; `condition` gönderilmezse mevcut durum korunur.
- `POST /{id}/return`: `condition` yoksa `good`; hem defter satırına hem varlığın `condition`'ına yazılır. `POST /{id}/lost`: kayıp tarihi atama tarihinden önce olamaz (`asset.return_before_issue`); defter satırında `returnCondition` boştur. Boşta (Unassigned) varlık kayıp işaretlenirse defter satırı olmadığı için `note` saklanmaz.
- `POST /{id}/acknowledge`: çalışan kaydı olmayan kullanıcı da 403 `asset.not_assignee` alır; zimmetli değilse 422 `asset.not_assigned`; ikinci kez 422 `asset.already_acknowledged`. Başka kiracının varlığı 404 (kiracı filtresi).
- `acknowledgedAt` UTC ISO-8601 anıdır (`DateTime`); defter tarihleri `DateOnly`.
- `EmployeeAssetHistoryDto` düz yapıdadır: `AssetAssignmentDto` alanları + `assetId, assetName, categoryId, serialNumber, assetTag` aynı nesnede.
- `search`: `%`/`_` joker değil sabit karakter; ad/seri/demirbaş no/marka/model üzerinde içerir araması.
- Demirbaş no tekilliği kiracı içindedir: handler büyük/küçük harf duyarsız kontrol eder, DB'de `(tenant_id, asset_tag)` filtreli benzersiz indeks güvencedir; aynı demirbaş no iki farklı kiracıda serbesttir.
- Kayıp (`lost`) için entegrasyon olayı **yoktur** (Documents'in AssetHandoverForm yeniden değerlendirmesi yalnız `AssetAssigned/AssetReturned` ile tetiklenir) — takip işi.

## 5. Web kapsamı
Envanter: arama kutusu, özet kartları (tıklayınca durum filtresi), yeni alanlar form ve tabloda, `pendingAssignment`/`lost`
durumları, "Kayıp bildir" eylemi, iade penceresine durum+not, varlık geçmişi çekmecesi/modalı, garanti/onay rozetleri.
"Zimmetlerim": "Teslim aldım" düğmesi (onaysız zimmette), geçmiş bölümü. Çalışan kartı Zimmet sekmesi: geçmiş listesi.
i18n: `tr` (kaynak), `en`, `de`. Mevcut Mantine bileşen/tema sistemine uyulur; yeni görsel kimlik üretilmez.

## 6. Kabul kriterleri (özet)
1. Mevcut Assets testleri + Notifications/Documents/Employee ilişkili testleri yeşil kalır.
2. Her yeni iş kuralı için sınır değerli birim testi; migration backfill'i entegrasyon testiyle doğrulanır
   (açık `Assigned` varlık → tam bir açık defter satırı).
3. Excel içe aktarma sonrası defter kayıtları oluşur (kapalı satır: kapalı atama).
4. `acknowledge` başka çalışan tarafından 403, ikinci kez kuralı hatası.
5. Web: tsc, ESLint, vitest, vite build yeşil; yeni bileşenler test edilir.
