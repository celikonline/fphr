# senseik-017 + senseik-018 — Demo Hijyeni + Audit Log — UYGULAMA PLANI

**Durum: Uygulandı (GATE 1/GATE 2 onaylandı). `a/demo-data-hygiene` (kart 017) ve `a/audit-log-fix` (kart 017 üzerine, kart 018) dallarında commit edildi; push yapılmadı.**

## Implementasyon Notu

- **Kart 017** — `web/src/pages/employees/org-chart.tsx`: varsayılan açılış derinliği 2→1, root `scrollIntoView` ile ortalanıyor (veri geldiğinde, şirket filtresi değiştiğinde, "Tümünü aç" sonrası). `OrgChartNodeDto`'ya `DepartmentName` eklendi (`EmployeeQueries.cs`/`EmployeeDtos.cs`, `LevelUnitFor("Department")` tekrar kullanıldı), FE'de ünvanın altında soluk bir satır. Seed: `DataSeeder.DemoEmployees.cs`'te yalnızca ilk HR rolündeki kullanıcı Headquarters'ta "İK Direktörü" oluyor, sonraki HR/HRSpecialist hesapları gerçek HR departmanına yönlendiriliyor (`ResolveDemoOrgUnit`, artık `internal` — `Sense.Tests.Integration`'a `InternalsVisibleTo` ile test edilebilir). Testler: `org-chart.test.ts` (derinlik semantiği), `OrgChartBuilderTests.cs` (DepartmentName çözümlemesi), `DemoEmployeeSeedTests.cs` (HR yönlendirme + ünvan dağılımı).
- **"juıouıouıuou" çöp kaydı**: kod tarafında hiçbir değişiklik yapılmadı/yapılamaz — hiçbir seed dosyasında üretilmiyor, demo/staging veritabanına elle girilmiş bir `EmployeeAvailability` kaydı. Ops adımı olarak kalıyor: `Note ILIKE '%juıouıouıuou%'` taraması + silme (`Cancel()` domain metodu). Ayrıca genel bir "restore from seed" öncesi temizlik adımı önerilir; kapsam bu oturumun dışında.
- **Kart 018** — `CurrentUserAccessor.UseSystem()` artık `DataSeeder.SeedAsync`'in tüm çağrı ağacını (tüm `SaveChangesAsync` çağrıları dahil) sarmalıyor; ham küçük harf `"system"` yerine bilinçli `"System"` actor'ü yazılıyor, `IdentityAuditLogInterceptor` ayrıca anonim düşüşte `ILogger` uyarısı veriyor. Tek batch SaveChanges tek actor taşıdığından kişi-bazlı atıf mekanik olarak mümkün değil — `DataSeeder.DemoAuditLog.cs` interceptor'ı atlayıp (`AuditLogEntry` zaten hariç) elle 6 gerçekçi kayıt ekliyor (Ahmet Yılmaz create, Ayşe Demir update, bir login, bir role_change, biri Import, biri Api kaynaklı). Yeni `AuditLogEntry.Source` (nullable, serbest string, migration `20260918142610_AddAuditLogEntrySource`) `IAuditSourceAccessor` (AsyncLocal, `TenantContext`/`CurrentUserAccessor` deseniyle aynı) üzerinden geliyor: `RequestContextMiddleware` Web/Api'yi ApiKeyId claim'ine göre set ediyor, `RunImportJobHandler` (DataTransfer) Import'u tüm import handler'ları için tek noktadan set ediyor, System ise interceptor'ın actor-bazlı fallback'inden geliyor (4 mevcut `UseSystem()` çağrı yeri hiç değişmedi). Maskeleme (`AuditLogQueries.Mask`) yalnızca `Details`'e dokunuyor — Actor/Source hiç maskelenmiyor. FE: `audit-log.tsx`'e Source kolonu, `audit.json`'da `"source":"Eylem Kaynağı"` (mevcut `"resource":"Kaynak"` ile çakışmıyor). Testler: `IdentityAuditLogInterceptorIntegrationTests.cs` (Web/Api/System/Import senaryoları + küçük harf "system" regresyonu), `GetAuditLogHandlerTests.cs` (Source maskelenmiyor regresyonu).
- Her iki dal için: Docker SDK konteyneriyle `dotnet build`/`dotnet test` (etkilenen projeler) yeşil; host `pnpm exec tsc --noEmit && pnpm exec eslint src && pnpm exec vite build` yeşil. `Sense.Modules.DataTransfer.Tests`/`Sense.Modules.Employee.Tests`'teki `IUserProfileLookup`/`PaymentsService` DI hatası bu oturumdan önce var olan, bu değişikliklerle ilgisiz bir test altyapısı eksikliği (ayrıntı: oturum handback notu).

## Kart 017 — Demo Hijyeni + Org Şeması (H-02/H-04/H-05)

### 1. "juıouıouıuou" Çöp Kaydı

`EmployeeAvailability` (Vardiya→Müsaitliğim) hiçbir seed dosyasında oluşturulmuyor — bu **kod değil, veritabanı veri temizliği** işi. Kayıt gerçekten canlı/staging ortamında elle girilmiş. Plan: demo/staging DB'de `Note ILIKE '%juıouıouıuou%'` taraması + silme/iptal (`Cancel()` domain metodu zaten var). Genel tarama: diğer "canlı çöp" alanları da (Leave request notları, serbest metin alanları) kontrol edilmeli. Kalıcı önlem (opsiyonel): demo öncesi "restore from seed" adımı.

### 2. Org Şeması — Kök Düğüm + Yatay Kaydırma

**Render sorunu, veri sorunu DEĞİL** — backend (`OrgChartBuilder.Build`) Ahmet Yılmaz'ı (Owner, `ManagerEmployeeId=null`) doğru şekilde root olarak dönüyor. Asıl sorun `org-chart.tsx`:
- İlk render'da derinlik-2'ye kadar açılıyor (root + departman yöneticileri + TÜM ekip üyeleri) — çok geniş bir alt-ağaç oluşuyor.
- `.tree` `display:flex; min-width:max-content` — genişlik içeriğe göre büzülüyor, root bu geniş alanın ortasına düşüyor.
- Scroll container varsayılan `scrollLeft=0` — root ortada ama kullanıcı en solu görüyor.

**Düzeltme (FE, düşük risk):** (1) Varsayılan açılış derinliğini 2'den 1'e düşür. (2) Veri geldiğinde root'u `scrollIntoView({inline:"center"})` ile view'a getir (mevcut arama-eşleşme deseni tekrar kullanılabilir). (3) "Tümünü aç" sonrası da root'u tekrar ortala. Dosya: `org-chart.tsx` (state/effect eklentisi), CSS değişikliği gerekmiyor.

### 3. Ünvan=Birim Karışıklığı (Ayşe Demir / Deniz Balci)

Kök neden: `DemoTitleFor`, HR rolüne sahip HERKESE aynı sabit ünvanı (`DemoHrTitle`) veriyor — ikisi de aynı title-node'a (aynı OrgUnit) düşüyor, gerçek departman bilgisi ağaç düğümünde gösterilmiyor (sadece popover'da).

**Düzeltme (veri modeli değişikliği YOK, sadece görüntü/gruplama):**
1. `OrgChartNodeDto`'ya Department seviyesi için `departmentName` alanı eklenir (mevcut `LevelUnitFor` yardımcısı tekrar kullanılır), FE'de ünvanın altına soluk bir departman satırı eklenir.
2. **Bunu anlamlı kılan minik seed düzeltmesi (opsiyonel, önerilir):** `DemoTitleFor`'da HR rolü için "ilk mi" kontrolü — sadece ilk HR'lı kişi `DemoHrTitle` alsın, ek kişiler normal HR departmanı ünvanlarına (İK Uzmanı vb.) yönlendirilsin (~10 satır değişiklik).
3. Sonuç: Ayşe Demir "Genel Merkez / İK Direktörü", Deniz Balci "İK Departmanı / İK Uzmanı" — çakışma görsel olarak kalkar.

## Kart 018 — Denetim Kaydı: Gerçek Kullanıcı ve Kaynak (E-01)

### 1. Kök Neden — Kesin Bulundu

`IdentityAuditLogInterceptor`: `currentUser.DisplayName ?? currentUser.Email ?? "system"`. `DataSeeder.cs`'in `SaveChangesAsync` çağrısı öncesinde `CurrentUserAccessor.Use(...)`/`UseSystem()` **hiç çağrılmıyor** (constructor'da `ICurrentUser` enjekte edilmemiş) — bu yüzden `AnonymousUser`'ın null değerlerine düşüp `"system"` yazılıyor. **83 kaydın tamamı DataSeeder'ın tek toplu `SaveChanges`'ından geliyor — bir "kullanıcı kaybolması" bug'ı değil, seed sürecinin hiçbir bağlam kurmadan çalışması.**

**Üretim riski:** Aynı mekanizma, `RequestContextMiddleware` çalışmayan her yolda sessizce `"system"`e düşer; bilinçli `SystemUser` ile bu fallback ayrım YAPMIYOR — gerçek bir hesap verebilirlik regresyonu, "seed gibi görünen normal kayıt"tan ayırt edilemez hale geliyor.

### 2. Düzeltme

1. `DataSeeder.cs` — `SaveChangesAsync` çağrıları `CurrentUserAccessor.UseSystem()` ile sarmalanmalı (en azından tutarlı "System" etiketi).
2. **Ek adım (önerilir, acceptance için gerekli):** Tek toplu `SaveChanges` AYNI actor'ü tüm batch'e yazdığı için kişi-bazlı gerçekçi atıf mekanik olarak mümkün değil — `DataSeeder.DemoAuditLog.cs` (yeni) ile interceptor'ı atlayıp elle kurgulanmış gerçekçi bir set (Ahmet Yılmaz "create", Ayşe Demir "update", bir login, bir role_change, biri Import kaynaklı, biri Api kaynaklı) eklenmeli.
3. `IdentityAuditLogInterceptor`'da `AnonymousUser` (gerçek, bilinçli `SystemUser` değil) durumunda bir `ILogger` uyarısı — üretimde ayrım için.

### 3. Eylem Kaynağı (Source) Alanı

`AuditLogEntry.Source` (string, serbest ama sabit kümeden — `AuditSources` sabitleri, DB'de enum/check-constraint DEĞİL, ileride kod tarafında tek satırla genişletilebilir):
- `Web` (varsayılan, authenticated + ApiKeyId yok), `Api` (ApiKeyId claim'i var), `Import` (toplu içe aktarım), `System` (background job — 4 mevcut `UseSystem()` çağrı yeri).
- **"Talep" için somut karşılık bulunamadı** — ürün sahibiyle netleştirilmeli, alan genişletilebilir tutulmalı.
- Mekanik: `TenantContext.cs`'teki `CurrentUserAccessor`/`AsyncLocal` deseninin aynısı, `RequestContextMiddleware`'de Web/Api ayrımı.
- **İsim çakışması uyarısı:** `audit.json`'da `"resource":"Kaynak"` zaten var (değişen agregat türü) — yeni "action source" alanı için AYNI etiketi kullanma, çakışır. Öner: `"source":"Eylem Kaynağı"`.

**Yan bulgu (kapsam dışı, not edildi):** `login`/`invite`/`role_change` action string'leri FE'de tanımlı ama backend'de HİÇBİR kod yolu bunları üretmiyor — giriş olayları şu an hiç audit'lenmiyor. Ayrı bir E-01/KVKK bulgusu olabilir, bu kartın kapsamı dışı.

### Test Planı

Web kaynaklı create, Api kaynaklı update, System kaynaklı arka plan işlemi, Import kaynaklı toplu işlem — her biri doğru `Actor`+`Source` kombinasyonunu doğrular. Regresyon: seed sonrası hiçbir kayıt ham küçük-harf `"system"` taşımamalı. Maskeleme regresyonu: yeni `Actor`/`Source` alanları maskelenmemeli (KVKK m.12 — "kimin yaptığı" görünür kalmalı).

## Kritik Dosyalar

**Kart 017:** `web/src/pages/employees/org-chart.tsx`, `EmployeeQueries.cs`, `DataSeeder.DemoEmployees.cs`, `SeedDataConstants.cs`

**Kart 018:** `IdentityAuditLogInterceptor.cs`, `AuditLogEntry.cs`, `TenantContext.cs`, `RequestContextMiddleware.cs`, `DataSeeder.cs`, `web/src/pages/audit-log.tsx`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § H-02, H-04, H-05, E-01. Agent board: `docs/agent-board/cards/senseik-017-demo-hijyen.json`, `senseik-018-audit-log.json`.*
