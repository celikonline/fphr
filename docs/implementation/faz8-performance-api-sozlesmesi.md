# Faz 8 — Performance API sözleşmesi (Hedef/OKR + değerlendirme dönemi, R0 iskeleti)

Bu doküman FE ikizinin `performance.service.ts` ve `pages/performance/*` için ihtiyaç duyduğu 14
ucu, koddaki **mevcut** şekliyle belgeler (§1-6 tur 3'ten, §7-14 tur 4'te eklendi; kod §1-6 için
değiştirilmedi, §14'ün `performance.review.immutable` hatası tur 4'te eklenen gerçek bir kod
değişikliğini yansıtıyor — bkz. "Değiştirilemezlik" bölümü). **Tur 5:** §2/§3/§5'e G-8-19/G-8-20
hataları eklendi (`performance.review.immutable`, `performance.goal.target_date_outside_cycle`) —
`GoalDto`/uç şekli değişmedi, yalnızca yeni hata kodları. Kaynak:
`Sense.Modules.Performance.Api.Controllers.{GoalsController, ReviewCyclesController,
ReviewFormsController}`, `Sense.Modules.Performance.Application.{Goals,ReviewCycles,ReviewForms}`.

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
- Hatalar: `performance.goal.title_required` (400, boş başlık). `reviewCycleId` verilmişse (G-8-20,
  tur 5): dönem bulunamazsa `performance.cycle.not_found` (404 — karar (a), yeni not-found kodu
  açılmadı), dönem `Closed` ise `performance.review.immutable` (409), `targetDate` dönemin
  `[startDate, endDate]` aralığı dışındaysa `performance.goal.target_date_outside_cycle` (400).

## 3. `POST /api/v1/performance/goals/{id}/progress/me`
- **Yetki:** `performance.my.goals`
- **Gövde:** `{ progress: int }` (0-100)
- **Yanıt:** `204 No Content`
- Hatalar: `performance.goal.progress_invalid` (400, aralık dışı), `performance.goal.not_updatable`
  (409, hedef zaten terminal — Completed/Cancelled/Archived), `403 Forbidden` (hedef caller'a ait
  değilse — **öz-servis kontrol sırası (karar b): önce 403, sonra aşağıdaki 409**),
  `performance.review.immutable` (409, hedef bir `reviewCycleId`'ye bağlıysa ve o dönem `Closed` ise,
  G-8-19, tur 5).

## 4. `GET /api/v1/performance/goals/team`
- **Yetki:** `performance.team.goals`
- **Yanıt:** `GoalDto[]` — **`employeeName` bu uçta dolu** (bkz. §6), diğer tüm `goals/*` uçlarında
  `null`'dır (FE zaten kimin hedefi olduğunu biliyor).
- Caller'ın `IDataScopeService.GetVisibleEmployeeIdsAsync("performance.team.goals")` ile görebildiği
  çalışanların hedefleri; görünür çalışan yoksa boş dizi.

## 5. `POST /api/v1/performance/goals/{id}/cancel`
- **Yetki:** `performance.goals.manage`
- **Yanıt:** `204 No Content`
- Hatalar: `performance.goal.not_cancellable` (409, hedef zaten Completed/Cancelled/Archived),
  `performance.review.immutable` (409, hedef bir `reviewCycleId`'ye bağlıysa ve o dönem `Closed` ise,
  G-8-19, tur 5). Karar (c): çalışanın **kendi** hedefini iptal ettiği bir uç yok — bu uç yalnız
  `performance.goals.manage` ile, iptal ilk sürümde yönetici/İK işidir (kasıtlı, bkz. §14 sonu).
- Not: `team-goals.tsx`'teki iptal aksiyonu bu ucu çağırır — `team/goals` GET'i `performance.team.goals`
  ile korunsa da iptal komutu ayrı yetki (`performance.goals.manage`) ister; FE iki yetkiyi de
  (`RequiresPermission` middleware zaten 403 döner) tek ekranda karışık kullanabilir, buton görünürlüğünü
  buna göre koşullandırmalı.

## 6. `GET /api/v1/performance/review-cycles/me`
- **Yetki:** `performance.my.reviews`
- **Yanıt:** `ReviewCycleDto[]`
- Draft dönemler hariç (henüz duyurulmamış); Active + Closed dönemler döner.

## 7. `GET /api/v1/performance/review-cycles`
- **Yetki:** `performance.reviews.view`
- **Sorgu:** `?status=` (opsiyonel, `draft`/`active`/`closed` — case-insensitive `Enum.Parse`, camelCase FE değeri de kabul edilir)
- **Yanıt:** `ReviewCycleDto[]` — tüm dönemler (Draft dahil), İK/yönetici tarafı.

## 8. `GET /api/v1/performance/review-cycles/{id}`
- **Yetki:** `performance.reviews.view`
- **Yanıt:** `ReviewCycleDto`
- Hata: `performance.cycle.not_found` (404).

## 9. `POST /api/v1/performance/review-cycles`
- **Yetki:** `performance.reviews.manage`
- **Gövde:** `{ name: string, startDate: "yyyy-MM-dd", endDate: "yyyy-MM-dd" }`
- **Yanıt:** `200 { id: string (guid) }`
- Hatalar: `performance.cycle.name_required` (400), `performance.cycle.invalid_date_range` (400, `endDate <= startDate`).
- Yeni dönem her zaman `Draft` durumunda oluşur.

## 10. `POST /api/v1/performance/review-cycles/{id}/activate`
- **Yetki:** `performance.reviews.manage`
- **Yanıt:** `204 No Content`
- Hata: `performance.cycle.not_activatable` (409, dönem zaten Draft değilse).
- `Draft -> Active`.

## 11. `POST /api/v1/performance/review-cycles/{id}/close`
- **Yetki:** `performance.reviews.approve` (**`.manage` değil** — kapatma daha yüksek yetki ister, terminal ve geri alınamaz)
- **Yanıt:** `204 No Content`
- Hatalar: `performance.cycle.already_closed` (409), `performance.cycle.not_closable` (409, dönem Active değilse).
- `Active -> Closed`, terminal; bkz. §"Değiştirilemezlik" — kapandıktan sonra döneme bağlı hiçbir form yayınlanamaz.

## 12. `GET /api/v1/performance/review-forms`
- **Yetki:** `performance.reviews.view`
- **Sorgu:** `?reviewCycleId=` (zorunlu, guid)
- **Yanıt:** `ReviewFormDto[]` — sorular `order` alanına göre sıralı döner.

## 13. `POST /api/v1/performance/review-forms`
- **Yetki:** `performance.reviews.manage`
- **Gövde:** `{ reviewCycleId: string (guid), title: string, questions: [{ text: string, weight: number }] }`
- **Yanıt:** `200 { id: string (guid) }`
- Hatalar: `performance.cycle.not_found` (404, `reviewCycleId` geçersiz), `performance.form.cycle_closed` (409, dönem
  Closed), `performance.form.title_required` (400), `performance.form.questions_required` (400, boş dizi),
  `performance.form.question_text_required` (400), `performance.form.weights_must_sum_to_100` (400, `weight`
  toplamı ≠ 100).
- Yeni form her zaman `Draft` durumunda, sorularıyla birlikte tek istekte oluşur (soru ekleme/çıkarma ayrı ucu yok —
  yeniden oluşturmak gerekir, form henüz Published değilken).

## 14. `POST /api/v1/performance/review-forms/{id}/publish`
- **Yetki:** `performance.reviews.manage`
- **Yanıt:** `204 No Content`
- Hatalar: `performance.form.not_found` (404), `performance.form.questions_required` (400, hiç soru yoksa),
  `performance.form.not_publishable` (409, form zaten Published), **`performance.review.immutable`** (409, formun
  bağlı olduğu dönem Closed — form kendisi hâlâ Draft'ta kalsa bile; bkz. `PublishReviewFormHandler`).
- `Draft -> Published`, formun soru/ağırlık yapısı için terminal (yeni soru gerekiyorsa yeni form oluşturulur,
  bu form güncellenmez — `performance.form.not_editable`).

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
tüketildiğinde otomatik geçilir (bkz. aşağıdaki "Değiştirilemezlik ve çıkış senaryosu" bölümü); FE'nin
bu durumu "düzenlenemez, salt-okunur" göstermesi yeterli, ayrı bir aksiyon sunmaz.

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

### `ReviewFormDto`
```
{
  id: string (guid)
  reviewCycleId: string (guid)
  title: string
  status: "draft" | "published"
  questions: ReviewFormQuestionDto[]   // her zaman order'a göre sıralı
}
```

### `ReviewFormQuestionDto`
```
{
  id: string (guid)
  text: string
  weight: number   // int, 0-100; formun tüm sorularının toplamı 100'dür
  order: number    // int, 0'dan başlar
}
```

## İzin anahtarları ve hata kodları — özet tablo

| Uç | İzin | Bilinen hata kodları |
|---|---|---|
| `GET goals/me` | `performance.my.goals` | — |
| `POST goals/me` | `performance.my.goals` | `performance.goal.title_required`, `performance.cycle.not_found`, `performance.review.immutable`, `performance.goal.target_date_outside_cycle` |
| `POST goals/{id}/progress/me` | `performance.my.goals` | `performance.goal.progress_invalid`, `performance.goal.not_updatable`, `403 Forbidden` (başkasının hedefi — kontrol sırası: önce 403), `performance.review.immutable` (dönem kapalı) |
| `GET goals/team` | `performance.team.goals` | — |
| `POST goals/{id}/cancel` | `performance.goals.manage` | `performance.goal.not_cancellable`, `performance.review.immutable` (dönem kapalı) |
| `GET review-cycles/me` | `performance.my.reviews` | — |
| `GET review-cycles` | `performance.reviews.view` | — |
| `GET review-cycles/{id}` | `performance.reviews.view` | `performance.cycle.not_found` |
| `POST review-cycles` | `performance.reviews.manage` | `performance.cycle.name_required`, `performance.cycle.invalid_date_range` |
| `POST review-cycles/{id}/activate` | `performance.reviews.manage` | `performance.cycle.not_activatable` |
| `POST review-cycles/{id}/close` | `performance.reviews.approve` | `performance.cycle.already_closed`, `performance.cycle.not_closable` |
| `GET review-forms` | `performance.reviews.view` | — |
| `POST review-forms` | `performance.reviews.manage` | `performance.cycle.not_found`, `performance.form.cycle_closed`, `performance.form.title_required`, `performance.form.questions_required`, `performance.form.question_text_required`, `performance.form.weights_must_sum_to_100` |
| `POST review-forms/{id}/publish` | `performance.reviews.manage` | `performance.form.not_found`, `performance.form.questions_required`, `performance.form.not_publishable`, `performance.review.immutable` |

Diğer (bu turda FE'nin kullanmadığı ama modülde var olan) uçlar: `GET/POST goals` ve
`GET goals/employee/{id}` (`performance.goals.manage`/`performance.goals.view`),
`POST goals/{id}/progress` (`performance.goals.manage`). Hata metinleri `SharedResource{,.en}.resx`'te
`performance.*` anahtarlarıyla lokalize edilir.

## Değiştirilemezlik ve çıkış senaryosu

- Kapanmış bir `ReviewCycle` bir daha `Active`'e dönmez (`CycleAlreadyClosed`/`CycleNotClosable`);
  360/1:1/yetkinlik gibi ileride eklenecek bireysel değerlendirme kayıtları da bu terminal duruma
  bağlanacak (4857 md.18 fesih delili + KVKK).
- **(Tur 4)** Dönem kapandıktan sonra o döneme bağlı hiçbir form artık yayınlanamaz —
  `POST review-forms/{id}/publish` `performance.review.immutable` (409) döner; bu, formun kendi
  Draft/Published durumundan **bağımsız** bir kilittir (form hâlâ Draft'ta kalıp dönem kapansa bile
  publish reddedilir). Kontrol `PublishReviewFormHandler`'da, Application katmanında yapılır (ReviewForm
  ve ReviewCycle ayrı aggregate, birbirine FK ile bağlı değil). Yayınlanmış bir formun kendisi zaten
  `performance.form.not_editable` ile değiştirilemez (`SetQuestions`, domain seviyesinde).
- `EmployeeTerminated` integration event'i tüketildiğinde (`EmployeeTerminatedHandler`) çalışanın açık
  (Open/InProgress) hedefleri **silinmez/iptal edilmez, `Archived`'e donar** — denetim izini korumak
  için. Idempotent: outbox yeniden teslimatında ikinci `Archive()` çağrısı no-op'tur.

## Bilinen açık işler / sınır dışı
- **(Tur 5 itibarıyla) `InitialPerformance` migration'ı main'de** (`3211e05`) — modül artık gerçek
  tablolu; önceki turların "migration henüz yok" notu geçerliliğini yitirdi.
- **360 derece geri bildirim, 1:1 notu, yetkinlik matrisi, eğitim, anket/eNPS, duyuru/takdir:** bu R0
  iskeletinin kapsamı dışında — K-5 (Survey/eNPS modül sınırı) hâlâ PO kararı bekliyor.
- `employeeName` yalnızca `goals/team`'de doldurulur; `goals/me` ve `goals/employee/{id}` yanıtlarında
  her zaman `null`'dır (FE bu alanlarda kendi employeeId/isim bağlamını zaten bilir).
- **Karar (c) — çalışan kendi hedefini iptal edemez:** ilk sürümde kasıtlı; iptal yalnız yönetici/İK'da
  (`performance.goals.manage`, §5). Bir `CancelMyGoalCommand`/uç **yok**, FE eklemeye çalışmamalı.
- **(Tur 5) G-8-19/G-8-20:** dönem (`ReviewCycle`) kapalıyken o döneme bağlı (`reviewCycleId` dolu)
  hiçbir hedefte ilerleme güncellenemez/iptal edilemez/yeni hedef oluşturulamaz
  (`performance.review.immutable`, 409); oluşturmada ayrıca `targetDate` dönemin `[startDate, endDate]`
  aralığı dışında olamaz (`performance.goal.target_date_outside_cycle`, 400). `reviewCycleId` boşsa
  (dönemsiz hedef) davranış değişmedi. Bkz. §2, §3, §5 ve `GoalCommands.cs`'teki `GoalReviewCycleGuard`.
