# ADR 0001 — Modüler Monolit

**Durum:** Kabul edildi — 2026-09-14

## Bağlam
SenseİK 20 iş modülü (Identity, Organization, Employee, Leave, Workflow, Attendance, Payroll, Assets, Expense, Training, Documents, Announcements, Performance, Compensation, Recruitment, Analytics, Notifications, Integrations, Billing, Ai) içeren çok kiracılı bir SaaS'tır. Ekip küçüktür; erken dönemde operasyon yükü düşük tutulmalıdır.

## Karar
Tek çalıştırılabilir (`Sense.Api`) + ayrı worker (`Sense.Worker`) olarak **modüler monolit**. Her modül kendi PostgreSQL şeması ve DbContext'ine sahiptir; modüller arası erişim yalnızca `*.Contracts` projeleri (okuma servisleri, integration event'ler) ve outbox üzerinden event ile yapılır. Doğrudan DbSet/Domain erişimi yasaktır (NetArchTest ile zorlanır).

## Sonuçlar
- Tek deploy, tek transaction sınırı (modül içi), basit lokal geliştirme.
- Modül sınırları DB'de görünür olduğundan ileride bir modül ayrı servise taşınabilir (`IEventBus` RabbitMQ adaptörü, YARP gateway hazır).
- Çapraz modül join yok; raporlama Analytics şemasındaki view'lar üzerinden.
