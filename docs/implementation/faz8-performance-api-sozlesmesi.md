# Faz 8 — Performance API sözleşmesi (Hedef/OKR + değerlendirme dönemi, R0 iskeleti)

Bu doküman FE ikizinin `performance.service.ts` ve `pages/performance/*` için ihtiyaç duyduğu 6
ucu, koddaki **mevcut** şekliyle belgeler (kod değiştirilmedi — uyumsuzluk görülürse burada değil,
raporda not düşülür). Kaynak: `Sense.Modules.Performance.Api.Controllers.{GoalsController,
ReviewCyclesController}`, `Sense.Modules.Performance.Application.{Goals,ReviewCycles}`.

Genel JSON kuralı (proje geneli): camelCase alan adları, enum'lar camelCase string
(`JsonStringEnumConverter(CamelCase)`), `null` alanlar yanıttan **düşürülür** (`WhenWritingNull`) —
FE tarafı eksik alanı `null` gibi ele almalı.

## 1. `GET /api/v1/performance/goals/me`
- **Yetki:** `performance.my.goals`
- **Yanıt:** `GoalDto[]`
- Caller'ın kendi hedefleri. `currentUser.EmployeeId` yoksa (ör. henüz Employee'ye bağlanmamış
  kullanıcı) boş dizi döner, hata değil.

## 2. `POST /api/v1/performance/goals/me`
- **Yetki:** `performance.my.goals`
- **Gövde:** `{ title: string, description?: string, targetDate: "yyyy-MM-dd", reviewCycleId?: string (guid) }`
- **Yanıt:** `200 { id: string (guid) }` (ham `Guid`, sarmalayıcı yok — `Dispatcher.Send` sonucu doğrudan serileşir)
- Hata: `performance.goal.title_required` (400, boş başlık).

## 3. `POST /api/v1/performance/goals/{id}/progress/me`
- **Yetki:** `performance.my.goals`
- **Gövde:** `{ progress: int }` (0-100)
- **Yanıt:** `204 No Content`
- Hatalar: `performance.goal.progress_invalid` (400, aralık dışı), `performance.goal.not_updatable`
  (409, hedef zaten terminal — Completed/Cancelled/Archived), `403 Forbidden` (hedef caller'a ait değilse).

## 4. `GET /api/v1/performance/goals/team`
- **Yetki:** `performance.team.goals`
- **Yanıt:** `GoalDto[]` — **`employeeName` bu uçta dolu** (bkz. §6), diğer tüm `goals/*` uçlarında
  `null`'dır (FE zaten kimin hedefi olduğunu biliyor).
- Caller'ın `IDataScopeService.GetVisibleEmployeeIdsAsync("performance.team.goals")` ile görebildiği
  çalışanların hedefleri; görünür çalışan yoksa boş dizi.

## 5. `POST /api/v1/performance/goals/{id}/cancel`
- **Yetki:** `performance.goals.manage`
- **Yanıt:** `204 No Content`
- Hata: `performance.goal.not_cancellable` (409, hedef zaten Completed/Cancelled/Archived).
- Not: `team-goals.tsx`'teki iptal aksiyonu bu ucu çağırır — `team/goals` GET'i `performance.team.goals`
  ile korunsa da iptal komutu ayrı yetki (`performance.goals.manage`) ister; FE iki yetkiyi de
  (`RequiresPermission` middleware zaten 403 döner) tek ekranda karışık kullanabilir, buton görünürlüğünü
  buna göre koşullandırmalı.

## 6. `GET /api/v1/performance/review-cycles/me`
- **Yetki:** `performance.my.reviews`
- **Yanıt:** `ReviewCycleDto[]`
- Draft dönemler hariç (henüz duyurulmamış); Active + Closed dönemler döner.

---

## DTO'lar

### `GoalDto`
```
{
  id: string (guid)
  employeeId: string (guid)
  title: string
  description: string | null
  targetDate: string  // "yyyy-MM-dd", DateOnly
  progress: number    // int 0-100
  status: "open" | "inProgress" | "completed" | "cancelled" | "archived"
  reviewCycleId: string (guid) | null
  employeeName: string | null   // yalnız goals/team doldurur (IEmployeeDirectory.GetContactsAsync, "Ad Soyad")
}
```
`archived` durumu kullanıcı tarafından tetiklenmez — yalnız `EmployeeTerminated` integration event'i
tüketildiğinde otomatik geçilir (bkz. §7); FE'nin bu durumu "düzenlenemez, salt-okunur" göstermesi
yeterli, ayrı bir aksiyon sunmaz.

### `ReviewCycleDto`
```
{
  id: string (guid)
  name: string
  startDate: string   // "yyyy-MM-dd"
  endDate: string     // "yyyy-MM-dd"
  status: "draft" | "active" | "closed"
}
```

## İzin anahtarları ve hata kodları — özet tablo

| Uç | İzin | Bilinen hata kodları |
|---|---|---|
| `GET goals/me` | `performance.my.goals` | — |
| `POST goals/me` | `performance.my.goals` | `performance.goal.title_required` |
| `POST goals/{id}/progress/me` | `performance.my.goals` | `performance.goal.progress_invalid`, `performance.goal.not_updatable`, `403 Forbidden` (başkasının hedefi) |
| `GET goals/team` | `performance.team.goals` | — |
| `POST goals/{id}/cancel` | `performance.goals.manage` | `performance.goal.not_cancellable` |
| `GET review-cycles/me` | `performance.my.reviews` | — |

Diğer (bu turda FE'nin kullanmadığı ama modülde var olan) uçlar: `GET/POST goals` ve
`GET goals/employee/{id}` (`performance.goals.manage`/`performance.goals.view`),
`POST goals/{id}/progress` (`performance.goals.manage`), `review-cycles` CRUD + `activate`/`close`
(`performance.reviews.{view,manage,approve}`), `review-forms/*` (aynı üçlü). Hata metinleri
`SharedResource{,.en}.resx`'te `performance.*` anahtarlarıyla lokalize edilir.

## Değiştirilemezlik ve çıkış senaryosu (kod kararı, tur 1'den — değişmedi)

- Kapanmış bir `ReviewCycle` bir daha `Active`'e dönmez (`CycleAlreadyClosed`/`CycleNotClosable`);
  360/1:1/yetkinlik gibi ileride eklenecek bireysel değerlendirme kayıtları da bu terminal duruma
  bağlanacak (4857 md.18 fesih delili + KVKK).
- `EmployeeTerminated` integration event'i tüketildiğinde (`EmployeeTerminatedHandler`) çalışanın açık
  (Open/InProgress) hedefleri **silinmez/iptal edilmez, `Archived`'e donar** — denetim izini korumak
  için. Idempotent: outbox yeniden teslimatında ikinci `Archive()` çağrısı no-op'tur.

## Bilinen açık işler / sınır dışı
- **Migration henüz yok** — `status/faz-08/MIGRATION-ISTEGI.md` (ADR-0005); migration girmeden bu
  uçlar gerçek bir veritabanına karşı 500/"no such table" verir, yalnız kod + mimari testler yeşildir.
- **360 derece geri bildirim, 1:1 notu, yetkinlik matrisi, eğitim, anket/eNPS, duyuru/takdir:** bu R0
  iskeletinin kapsamı dışında — K-5 (Survey/eNPS modül sınırı) hâlâ PO kararı bekliyor.
- `employeeName` yalnızca `goals/team`'de doldurulur; `goals/me` ve `goals/employee/{id}` yanıtlarında
  her zaman `null`'dır (FE bu alanlarda kendi employeeId/isim bağlamını zaten bilir).
