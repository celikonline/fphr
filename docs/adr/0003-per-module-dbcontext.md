# ADR 0003 — Modül başına DbContext ve PostgreSQL şeması

**Durum:** Kabul edildi — 2026-09-14

## Karar
Tek PostgreSQL veritabanı (`senseik`), modül başına şema (`identity`, `org`, `employee`, `leave`, …) ve modül başına `DbContext` (`ModuleDbContext<TSelf>` taban sınıfı). Migration geçmişi şema başına tutulur. Migration'lar `Sense.Migrator` ile uygulanır; API otomatik migrate etmez.

## Sonuçlar
- Modül sınırı veritabanında görünür; ekipler paralel migration üretebilir.
- Çapraz şema join yalnızca `analytics` şemasındaki view'larda (tek izinli nokta).
- Modüller arası tutarlılık outbox/inbox ile eventual.
