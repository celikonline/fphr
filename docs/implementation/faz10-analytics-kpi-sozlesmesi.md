# Faz 10 — Analytics KPI uç noktaları sözleşmesi

Backend: `src/Modules/Analytics/**`. Tüm uçlar `[Authorize]` + `analytics.kpi.view` izni ister; tenant filtresi
`ITenantContext` üzerinden zorunludur. Birim yöneticisi kısıtı `IDataScopeService.GetVisibleEmployeeIdsAsync`
(`null` = Company kapsamı/tüm çalışanlar, dolu küme = yalnız görünür çalışanlar) ile uygulanır — hiçbir KPI kapsam
dışı çalışan verisini sızdırmaz.

## `GET /api/v1/analytics/kpis/workforce?from=&to=`
5 KPI (career path ratio, gender ratio, turnover, early turnover, gender-by-unit) — `KpiResultDto[]`. Değişmedi.

## `GET /api/v1/analytics/kpis/leave-usage?year=&orgUnitId?` (S-15)
```json
{ "totalEntitledDays": 20.0, "usedDays": 5.0, "usageRate": 0.25, "byLeaveType": [{ "leaveTypeName": "Yıllık İzin", "usedDays": 5.0 }] }
```
- Veri: `Leave.Contracts/ILeaveUsageReader.GetAnnualUsageAsync` (S-18, Faz 5 sahipliği).
- `orgUnitId` verilirse `Employee.Contracts/IEmployeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync` ile o birimin
  çalışan id listesine daraltılır, **G-10-16:** bu port `Terminated` dışındakileri döndürür (Draft/Suspended dahil)
  — `ListActiveEmployeeIdsAsync`'in `Active`-only kümesiyle kesiştirilir (birim toplamı şirket toplamıyla tutarlı
  kalsın diye); kısıtlı kapsamdaki çağıran için ayrıca kendi görünürlüğüyle kesişim alınır.
- `usageRate`: hak 0 ise 0 (bölme hatası yok).
- **G-10-17 kapsam sınırı:** yalnız sorgu anında `Active` ve kapsam dahilindeki çalışanları sayar; dönem içinde
  ayrılan/askıya alınan çalışanın o dönemdeki verisi dahil edilmez (dönem-farkındalıklı sorgu backlog B-10-03).

## `GET /api/v1/analytics/kpis/overtime-cost?year=&month=&orgUnitId?` (S-17)
```json
{ "year": 2026, "month": 9, "totals": [{ "currency": "TRY", "amount": 1500.00 }], "employeeCount": 1, "excludedEmployeeCount": 0 }
```
- Ek yetki: `employee.salary.view` (`EmployeePermissions.SalaryViewKey`) — maaş hassas alan, `analytics.kpi.view`
  ile birlikte **ikisi de** gerekir (`[RequiresPermission]` AllowMultiple → AND; ikisinden biri eksikse 403).
- Çalışan listesi kaynağı: Company kapsamında (`GetVisibleEmployeeIdsAsync` → `null`) `Employee.Contracts/
  IEmployeeDirectory.ListActiveEmployeeIdsAsync`/`ListActiveEmployeeIdsInOrgUnitsAsync` (Payroll'un 11.3.07 P3
  için zaten eklediği port — S-17 için yeni bir cross-module port açmaya gerek kalmadı); kısıtlı kapsamda
  `IDataScopeService` sonucu birim filtresiyle kesişir. **G-10-16:** `orgUnitId` verilen her dalda birim listesi
  ayrıca `ListActiveEmployeeIdsAsync`'in `Active`-only kümesiyle kesiştirilir (`ListActiveEmployeeIdsInOrgUnitsAsync`
  yalnız `Terminated`'ı dışlar, Draft/Suspended dahil — kesişim olmadan birim toplamı şirket toplamıyla uyuşmazdı).
- **G-10-17 kapsam sınırı:** yalnız sorgu anında `Active` ve kapsam dahilindeki çalışanları sayar; dönem içinde
  ayrılan/askıya alınan çalışanın o dönemdeki verisi dahil edilmez (dönem-farkındalıklı sorgu backlog B-10-03).
- Hesap: `Attendance.Contracts/IAttendanceMonthlySummaryReader.OvertimeHours` × saatlik ücret × 1,5
  (`OvertimeCostCalculator`, 4857 s. İş K. md.41/63). Saatlik ücret yalnız `Monthly` (`Amount/225`), `Daily`
  (`Amount/7.5`), `Hourly` (`Amount`) ödeme düzenlerinde tanımlı; `Weekly`/`Yearly` ve maaş kaydı olmayan
  çalışan `excludedEmployeeCount`'a sayılır, hesaba girmez.
- Para birimi başına ayrı toplam (`totals[]`) — çevrim yapılmaz.
- Attendance özetinde hiç kaydı olmayan çalışan (`IAttendanceMonthlySummaryReader.GetAsync` sonucunda yok) ne
  dahil ne hariç sayılır — liste dışı kalır (Attendance'ın "veri yok" ile "0 gün" ayrımı, S-8 sözleşmesi).

## FE'ye not
`leave-usage`/`overtime-cost` 403/404 durumunda kart gizlenmeli (`overtime-cost` için 403, maaş izni yoksa
beklenen normal akış — hata değil, görünürlük kuralı).

## S-21 (ADR-0020) — devamsızlıkta tek otoriter kaynak
`Leave.Absence` "devamsızlık günü" için **tek** otoriter kaynaktır; `Attendance.AttendanceAnomaly` hiçbir
rapora/KPI'ya devamsızlık olarak girmez, otomatik dönüşüm yoktur (çift sayım riski, backlog #12). Anomali
sayısı ayrı bir operasyon metriği olarak eklenebilir ama adında/etiketinde "devamsızlık" geçemez. Tur 4
taraması (`grep -rn "Anomaly" src/Modules/Analytics src/Modules/Reporting`) **temiz**: Analytics/Reporting'de
`AttendanceAnomaly`'e hiç referans yok, herhangi bir KPI/alan bu kaynaktan devamsızlık üretmiyor — devamsızlık
KPI'sı bu fazda henüz hiç yazılmadı (kod tabanında `Absence` sözcüğü de Analytics/Reporting'de geçmiyor).
İleride bir devamsızlık KPI'sı eklenirse veri kaynağı `Leave.Absence` olmalı, `AttendanceAnomaly` kullanılmamalı.
