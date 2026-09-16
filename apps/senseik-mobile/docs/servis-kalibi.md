# Mobil servis kalıbı

Sonraki iş dikeyleri (Attendance, Payroll, Approvals, Dashboard, …) bu kalıbı kopyalar. Referans
uygulama: **Leave** (`src/services/LeaveService.ts` + `src/screens/app/LeaveScreen.tsx` +
`src/__tests__/services/LeaveService.test.ts`). İkinci örnek: **Attendance**
(`src/services/AttendanceService.ts` + `src/screens/app/AttendanceScreen.tsx`). Üçüncü örnek:
**Approvals** (`src/services/ApprovalsService.ts` + `src/screens/app/ApprovalsScreen.tsx` +
`src/__tests__/services/ApprovalsService.test.ts`) - farkı: kendi self-servis verisi yerine web
`workflow.service.ts`'in `/workflow/inbox` uçlarını (başka birinin onayına düşen talepler) tüketiyor
ve bir "karar" gönderiyor (`decide`, gerekçe zorunlu olduğunda `comment` alanı doluyor); `InboxTaskDto`
bugün talep tutarı/tarihi taşımıyor (backlog B-10-02, Workflow sözleşmesi genişleyince eklenecek),
kart bu yüzden yalnız talep sahibi + tür + adım numarasını gösteriyor. Dördüncü örnek: **Profile**
(`src/services/ProfileService.ts` + `src/screens/app/ProfileScreen.tsx` +
`src/__tests__/services/ProfileService.test.ts`) - farkı: **salt okunur** bir kalıp, form/karar yok;
web `employees.service.ts`'in `GET /employees/me` (`getMyEmployee`) ucunu tüketir ama `EmployeeDetail`
tipinin tamamını değil, ekranda gösterilen alt kümesini (`MyProfile`) tanımlar — KVKK veri minimizasyonu
gereği TC kimlik, maaş, adres gibi hassas alanlar tipe bile alınmaz. 404 (çalışan kaydı yok) ayrı bir
durum olarak ele alınır ("Çalışan kaydınız bulunmuyor"), genel hata bandından farklı gösterilir.

## Katmanlar

```
lib/apiClient.ts  →  services/<Modül>Service.ts  →  (opsiyonel) store/<modül>Store.ts  →  screens/app/<Ekran>.tsx
```

### 1. `lib/apiClient.ts` — dokunma

Tek axios instance; auth header, korelasyon id'si, 401→refresh-token akışı burada zaten kurulu.
Yeni bir servis bunu **olduğu gibi** import eder (`import { apiClient } from "@/lib/apiClient"`),
kendi axios instance'ını açmaz.

### 2. `services/<Modül>Service.ts` — her modülün kendi dosyası

- Web tarafındaki karşılığıyla (`web/src/services/<modül>.service.ts`) **aynı API uçlarını**,
  aynı DTO alan adlarını (camelCase) kullanır — iki taraf aynı backend sözleşmesini tüketir, tipleri
  kopyalarken web dosyasına bakılır.
- Yalnızca **self-servis** uçları kapsar (`/me/...` — bakiye, taleplerim, özet). Ekip/İK-yönetici
  uçları (`/team/...`, `/employees/{id}/...`) mobilde bugün yok; gerekirse ayrı bir tur/karar.
- Dosya başına kısa bir JSDoc yorumu: hangi backend controller'ı yansıtıyor, hangi web servisiyle
  aynı uçları paylaşıyor, kapsam dışı bırakılan neyse (bkz. `LeaveService.ts`, `AttendanceService.ts` başı).
- Nullable/opsiyonel backend alanları `field?: T | null` olarak yazılır (API `WhenWritingNull` ile
  null alanları JSON'dan **çıkarıyor** — eksik gelen alan `undefined` olur, TS tipi hem `null` hem
  `undefined`'ı kapsamalı; bkz. `web/src/services/reports.service.ts` ve bu depodaki `CLAUDE.md`).
- Sınıf + tekil örnek dışa aktarımı: `export class XService { ... }` / `export const xService = new XService();`
  (bkz. `LeaveService`, `AuthService`).
- Hata sarmalama yok: `apiClient` zaten axios hatasını olduğu gibi fırlatır, servis metodu
  `try/catch` **eklemez** (AuthService'in `login`/`logout` gibi yan etkili — token temizleme —
  metotları istisna; salt okuma/self-servis metotları hatayı olduğu gibi yukarı taşır).

### 3. `store/<modül>Store.ts` — yalnızca ekranlar arası paylaşılan state için

`authStore.ts` gibi bir Zustand store **yalnızca** state birden fazla ekran/navigasyon arasında
paylaşılıyorsa açılır (oturum durumu gibi). Tek ekranlık self-servis dikeyler (Leave, Attendance)
için store **açılmaz** — veri `useState`/`useEffect` ile ekranın kendisinde tutulur
(bkz. `LeaveScreen.tsx`, `AttendanceScreen.tsx`). Gerçekten paylaşılan bir state ihtiyacı çıkarsa
yeni bir store dosyası bu şablonla açılır, yeni bir state kütüphanesi getirilmez.

### 4. `screens/app/<Ekran>.tsx` — ekran

- Yükleniyor: tam ekran `ActivityIndicator` (ilk yüklemede), `RefreshControl` (pull-to-refresh).
- Hata: üstte bir hata bandı (`errorBanner`/`errorText` stilleri) + Türkçe, kullanıcı dostu mesaj;
  ham hata metni **gösterilmez**.
- Form gönderimi hatası: `Alert.alert("Hata", "...")`, buton `submitting` state'iyle devre dışı.
- Tarih biçimi: `toLocaleDateString("tr-TR", {...})` (bkz. `formatDateOnly` yardımcı fonksiyonu);
  API'ye giden tarihler `DateOnly` (`yyyy-MM-dd`) string olarak kalır, biçimlendirme yalnızca
  ekranda gösterirken yapılır.
- Stiller: `styles/colors.ts` + `styles/spacing.ts` + `styles/typography.ts` üzerinden
  `StyleSheet.create` — yeni bir tema/tasarım sistemi eklenmez.
- Boş liste: sade bir `Text` + `emptyText` stili (yeni bir "empty state" bileşeni açılmaz).

### 5. Test — `src/__tests__/services/<Modül>Service.test.ts`

`AuthService.test.ts` / `LeaveService.test.ts` emsali: `jest.mock("@/lib/apiClient")`,
`apiClient.get`/`post` çağrısının **tam URL + body** ile yapıldığını doğrula, en az bir başarı +
bir hata-yayılımı (`rejects`) senaryosu. `apiClient` mock'landığı için gerçek ağ isteği atılmaz,
tenant/permission süzme backend tarafında olduğundan mobil testte tekrar edilmez.

## Kapsam dışı (bilinçli olarak bu turlarda yazılmadı)

- Offline/queue senkronizasyonu, arka planda yenileme — backend zaten senkron cevap veriyor.
- Ekip/İK-yönetici mobil ekranları — self-servis öncelik, yönetici akışları web'de kalır.
- Push bildirimi tetikli otomatik yenileme — `PushNotificationService.ts` bugün yalnızca token
  kaydı yapıyor, ekran-özel dinleyici bu turlarda eklenmedi.
