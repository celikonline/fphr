# BGYS / ISO 27001 / BİGR Uyum Boşluk Analizi (2026-09-22)

## Kapsam ve yöntem

Bu doküman SenseİK kod tabanının (`src/`, `docs/`, `k8s/`, `infra/`, CI iş akışları) mevcut durumunu üç referansla karşılaştırır:

- **BGYS** — ISO/IEC 27001'in yönetim sistemi maddeleri (4-10: bağlam, liderlik, planlama, destek, işletim, performans değerlendirme, iyileştirme). Bunlar çoğunlukla **süreç/doküman** gereksinimleridir, kod taramasıyla doğrulanamaz; bu doküman yalnızca kod tabanından görülebilen izleri (politika dokümanı, risk kaydı, iç denetim planı vb.) raporlar.
- **ISO/IEC 27001:2022 Ek A** — 93 teknik/organizasyonel kontrol (A.5 Kurumsal, A.6 İnsan, A.7 Fiziksel, A.8 Teknolojik). Aşağıdaki tabloda ilgili kontrol grupları fonksiyonel alanlara göre gruplandı; her 93 maddeyi tek tek listelemek yerine, koddan doğrulanabilen alanlara odaklanıldı.
- **BİGR** — Cumhurbaşkanlığı 2019/12 sayılı Genelge'ye dayanan "Bilgi ve İletişim Güvenliği Rehberi": kamu kurumları ve kritik altyapı işletmecileri için daha reçeteli teknik/organizasyonel önlemler seti (ağ güvenliği, sunucu güvenliği, günlük (log) yönetimi, veri sızıntısı önleme, USOM'a olay bildirimi, yerli/milli ürün tercihi, KEP/e-imza vb.). SenseİK bir kamu kurumu değil, ancak kamu kurumlarına hizmet vermek isteyen bir SaaS için BİGR uyumu ticari bir gereksinim olabilir.

**Yöntem:** kod tabanı bir araştırma turuyla tarandı (12 fonksiyonel alan: kimlik doğrulama, yetkilendirme, denetim kaydı, şifreleme, loglama/izleme, yedekleme/DR, KVKK, bağımlılık/güvenlik açığı yönetimi, değişiklik yönetimi, varlık/ağ güvenliği, olay müdahale, fiziksel/organizasyonel). Her bulgu dosya:satır referansıyla doğrulandı; üç kritik bulgu (yedekleme/şifreleme pazarlama iddiası, TCKN şifrelemesi, rıza kaydı DI bağlantısı) ayrıca elle tekrar doğrulandı. Bu bir **sertifikasyon denetimi değildir** — resmi bir ISO 27001 belgelendirmesi için akredite bir denetim kuruluşu gerekir; bu doküman yalnızca iç hazırlık için boşlukları haritalar.

**Durum kodları:** ✅ Uygulanmış ve koddan doğrulanabilir · ⚠️ Kısmen uygulanmış / tasarım var ama uçtan uca bağlı değil · ❌ Bulunamadı.

---

## 🔴 Kritik ve acil bulgular

Bunlar önceliklendirilmiş listeden önce ayrıca vurgulanıyor çünkü hukuki/itibari risk taşıyor:

1. **Pazarlama sayfası, koddaki gerçeklikle uyuşmayan iddialar içeriyor** (`web/public/locales/tr/trust.json`):
   - Satır 24: *"Veriler düzenli olarak yedeklenir ve olağandışı durumlara karşı kurtarma süreçleri işletilir."* — kod tabanında (k8s, infra, docs) **hiçbir yedekleme betiği, CronJob, DR planı veya RPO/RTO tanımı bulunamadı.**
   - Satır 8: *"...veritabanında ise depolama katmanında şifrelenir."* — Postgres/MinIO için depolama-katmanı şifrelemesi (TDE/LUKS/SSE) yapılandırması kod tabanında yok (altyapı katmanı olabilir, ama doğrulanamıyor).
   - Aynı satır: *"Hassas özlük alanları (TCKN, IBAN, sağlık bilgileri) yalnızca yetkili kullanıcılara maskesiz gösterilir."* — IBAN alan-düzeyinde şifreli (`BankAccount.ProtectedIban`), **TCKN düz metin sütun** (`Employee.NationalId`, `EmployeeDbContext.cs:103`), sağlık verisi için ayrı bir şifreli alan yok. Maskeleme (`MaskNationalId`) var ama bu "şifreleme" değil, görüntüleme maskesi.
   - **Öneri:** bu sayfa metni ya gerçek duruma çekilsin ya da altında açıklanan önlemler gerçekten uygulansın; aksi halde KVKK md. 12 ("gerekli her türlü teknik ve idari tedbirleri alır" beyanı) ve tüketici/haksız rekabet mevzuatı açısından yanıltıcı beyan riski var.
2. **Yedekleme/DR tamamen yok.** Prod veritabanı tek bir k8s/Docker volume'de; herhangi bir felaket/veri kaybı senaryosunda geri dönüş yolu yok. Bu, ISO 27001 A.8.13 (Bilgi yedekleme) ve BGYS md.8 (operasyonel kontrol) için en temel gereksinimdir — **P0**.
3. **TCKN düz metin saklanıyor**, yalnızca IBAN ve TOTP sırrı alan-düzeyinde şifreli. TCKN, KVKK'da "özel nitelikli olmayan ama kimlik tespit edici" temel bir kişisel veridir; ISO 27001 A.8.24 (Kriptografi kullanımı) ve BİGR'in kişisel veri sızıntısı önleme maddeleri kapsamında ele alınmalı.
4. **Rıza kaydı (`ConsentRecorder`) yazılmış ama devrede değil** — `IdentityModule`'de DI kaydı yok, gerçek bir EF tablosu/migration'ı yok. KVKK açık rıza/aydınlatma kayıtları bugün **hiçbir yerde kalıcı olarak tutulmuyor**.

---

## 1. BGYS yönetim sistemi (ISO 27001 md. 4-10)

| Madde | Konu | Durum | Not |
|---|---|---|---|
| 4 | Kuruluşun bağlamı, ilgili tarafların ihtiyaçları, BGYS kapsam belgesi | ❌ | Kod tabanında/`docs/`'ta bir BGYS kapsam belgesi, ilgili taraf analizi yok. |
| 5 | Liderlik, bilgi güvenliği politikası, roller ve sorumluluklar | ❌ | Yazılı bir "Bilgi Güvenliği Politikası" dokümanı bulunamadı. Teknik roller (kod: `SystemRoleDefinitions`) var ama bu bir BGYS rol/sorumluluk atama dokümanı değil. |
| 6 | Risk değerlendirme ve işleme metodolojisi, risk kaydı, BGYS hedefleri | ❌ | Bir risk kaydı (risk register), risk işleme planı veya BGYS hedef dokümanı yok. `docs/adr/` mimari kararları tutuyor ama risk değerlendirmesi değil. |
| 7 | Destek: kaynaklar, yetkinlik, farkındalık, iletişim, dokümante bilgi kontrolü | ❌ | Güvenlik farkındalık eğitimi/iletişim planı dokümante değil. `docs/` genel olarak iyi dokümante (mimari, gözlemlenebilirlik, CI/CD) ama BGYS doküman kontrolü (versiyon, onay, gözden geçirme periyodu) yok. |
| 8 | Operasyonel planlama ve kontrol, risk değerlendirmesinin işletilmesi | ⚠️ | Değişiklik yönetimi (CI/CD, PR süreci) var (bkz. §9 aşağıda) ama resmi bir "operasyonel risk kontrolü" süreci olarak dokümante değil. |
| 9 | İzleme, ölçme, iç denetim, yönetim gözden geçirmesi | ⚠️ | Teknik izleme (Serilog/OTel/Sentry/Grafana) güçlü (bkz. §5), ama BGYS anlamında iç denetim planı/yönetim gözden geçirme toplantı kaydı yok. |
| 10 | Uygunsuzluk, düzeltici faaliyet, sürekli iyileştirme | ❌ | Yapılandırılmış bir DÖF (düzeltici/önleyici faaliyet) süreci/kaydı bulunamadı; GitHub Issues/agent-board'un bu amaçla kullanıldığına dair bir iz yok. |

**Özet:** BGYS'nin kendisi (yönetim sistemi olarak) henüz kurulmamış — bu beklenen bir durum, çünkü BGYS bir kod projesi değil, bir kurumsal süreçtir. Aşağıdaki teknik bulgular BGYS'nin "Ek A kontrolleri" ayağını değerlendirir; md. 4-10 için ayrı bir kurumsal çalışma (politika yazımı, risk atölyesi, iç denetim takvimi) gerekir ve bu genellikle kod dışı bir ekip/danışman işidir.

---

## 2. ISO 27001 Ek A kontrolleri — fonksiyonel alanlara göre

| Alan | İlgili Ek A maddeleri (örnek) | Durum | Kanıt / Boşluk |
|---|---|---|---|
| **Parola politikası ve kimlik doğrulama** | A.5.17, A.8.5 | ✅ | Min. 10 karakter, 4 sınıftan ≥3'ü, yaygın parola listesi reddi (`PasswordPolicy.cs:8-53`); PBKDF2 hash (`AspNetPasswordHasher`); 5 başarısız denemede 15 dk kilit (`IdentityOptions.cs:8-10`, `Login.cs:96-105`). Parola geçmişi/tekrar engeli **yok**. |
| **Çok faktörlü kimlik doğrulama** | A.8.5 | ✅ | TOTP (RFC 6238), sır AES-GCM şifreli (`TotpService.cs`, `User.TotpSecretEncrypted`). WebAuthn/FIDO2 **yok**, SSO/OIDC/SAML **yok**. |
| **Oturum/token yönetimi** | A.8.5 | ⚠️ | JWT RS256, 15 dk erişim / 30 gün yenileme, rotasyon + tekrar-kullanım tespiti (`RefreshAndLogout.cs:40-51`). Web'de refresh token `sessionStorage`, access token `localStorage` — bilinen/dokümante XSS riski (ADR 0009), httpOnly cookie'ye geçiş planlanmış ama yapılmamış. |
| **Erişim kontrolü (RBAC)** | A.5.15, A.5.18, A.8.2, A.8.3 | ✅ | `[RequiresPermission]` (208 kullanım), `SystemRoleDefinitions`, `DataScopeService` (Self/Team/Department/Location/Company kapsamı), tenant izolasyonu EF global query filter ile (`ModuleDbContext.cs:49-53`). Platform-admin/tenant-admin ayrımı impersonation onay akışıyla korunuyor. |
| **Ayrıcalıklı erişim yönetimi** | A.8.2 | ✅ | Impersonation: talep→onay→başlat→iptal akışı, ayrı denetim olayları (`ImpersonationCommands.cs`). "Break-glass" acil erişim için ayrı bir log/uyarı mekanizması **yok** (impersonation denetimiyle örtüşüyor ama adı konmamış). |
| **Denetim kaydı (audit log)** | A.8.15 | ⚠️ | `AuditLogInterceptor` 80+ dosyada `IAuditLogged` ile tetikleniyor; alan-düzeyi diff, aktör/tenant/kaynak bilgisi var. **DB-düzeyinde değişmezlik (immutability) yok** (yalnızca kod yorumu/konvansiyon), retention/arşivleme politikası **yok**, kriptografik bütünlük (hash zinciri) **yok**. |
| **Kriptografi** | A.8.24 | ⚠️ | AES-GCM alan şifrelemesi yalnızca IBAN ve TOTP sırrı için; anahtar yönetimi prod'da `Security:EncryptionKey` env var (Key Vault/HSM entegrasyonu **yok**, yalnızca yorum satırında "prod: Key Vault" notu var). TCKN, maaş, sağlık verisi şifrelenmiyor. |
| **Ağ güvenliği** | A.8.20, A.8.21, A.8.22 | ✅ (k8s prod) | Varsayılan-reddet NetworkPolicy + bileşen bazlı izin listesi (`k8s/overlays/prod/network-policies.yaml`), TLS sonlandırma cert-manager ile, `/metrics` iç ağa kısıtlı (doküman düzeyinde). Placeholder domain adları (`api.example.com`) — gerçek prod topolojisi doğrulanamadı. |
| **Sır/gizli bilgi yönetimi** | A.8.24 | ⚠️ | Dev sırları `appsettings.json`/`.env.example`'da açık (beklenen, dev-only); `.env` gerçek dosyası git'e girmemiş (`.gitignore`). Prod'da k8s "plain kustomize secretGenerator" kullanılıyor; Sealed Secrets/External Secrets/Vault **önerilmiş ama uygulanmamış** (`k8s/README.md:168-176`). |
| **Günlükleme ve izleme** | A.8.15, A.8.16 | ✅ | Serilog+OTel+Sentry+Grafana/Prometheus/Loki; her log `TenantId`/`UserId`/`CorrelationId` taşıyor; PII loglanmama kuralı dokümante (`docs/observability.md:177-178`) ama otomatik redaksiyon/scrubbing aracı yok, yalnızca kodlama kuralı. Loki/Prometheus retention 7 gün — uzun vadeli arşiv yok. |
| **Yedekleme** | A.8.13 | ❌ | Postgres/MinIO için hiçbir yedekleme betiği/CronJob/DR planı yok. **Kritik bulgu #2'ye bakın.** |
| **Bilgi varlıkları envanteri** | A.5.9 | ❌ | Donanım/yazılım/bulut kaynak envanteri dokümante değil. |
| **Tedarikçi ilişkileri** | A.5.19, A.5.20, A.5.21, A.5.22 | ❌ | Üçüncü taraf/tedarikçi listesi, tedarikçi güvenlik değerlendirmesi dokümante değil. Kod tabanında yalnızca genel bir webhook/entegrasyon modülü ve ileride planlanan e-imza/KEP sağlayıcı portları var. |
| **Bağımlılık/güvenlik açığı yönetimi** | A.8.8 | ⚠️ | CodeQL (SAST) + Dependabot (NuGet/npm/Actions/Docker) haftalık — iyi temel. `dotnet list package --vulnerable`, `npm audit`, container image tarama (Trivy/Grype), secret-scanning (gitleaks) **yok**. |
| **Güvenli geliştirme yaşam döngüsü** | A.8.25, A.8.28, A.8.29 | ✅ | PR'da build+test+format+mimari testler+entegrasyon testleri (gerçek Postgres/Redis); CodeQL. Branch protection dokümante edilmiş (`docs/ci-cd.md:158-170`) ama GitHub üzerinde fiilen etkin olup olmadığı bu oturumdan doğrulanamadı. |
| **Değişiklik yönetimi** | A.8.32 | ⚠️ | Commit-lint + husky + PR zorunluluğu var; ayrı bir **deployment onay kapısı** (örn. GitHub Environments + required reviewers) yok — "Deployment bu workflow'un kapsamında değil (Faz 0 kararı)" (`docs/ci-cd.md:53`). |
| **Olay müdahale** | A.5.24, A.5.25, A.5.26, A.5.27 | ❌ | Yazılı bir olay müdahale süreci, güvenlik iletişim kişisi, ihlal bildirim prosedürü (KVKK md.12 gereği Kurul'a 72 saat içinde bildirim dahil) bulunamadı. |
| **İş sürekliliği** | A.5.29, A.5.30 | ❌ | Yedekleme yokluğuyla birlikte, iş sürekliliği planı da yok. |
| **Fiziksel/barındırma güvenliği** | A.7.x | ❌ (kod dışı) | Barındırma sağlayıcısı/veri merkezi bilgisi kod tabanında yok; bu genelde bulut sağlayıcının kendi sertifikasyonuna (örn. ISO 27001 belgeli bir bulut) devredilir — hangi sağlayıcı kullanıldığı netleşmeden değerlendirilemez. |

---

## 3. BİGR'e özgü ek maddeler

BİGR, ISO 27001 Ek A ile büyük ölçüde örtüşür ama kamu/kritik altyapı odaklı bazı ek/daha reçeteli maddeler getirir:

| BİGR alanı | Durum | Not |
|---|---|---|
| Günlük (log) yönetimi — asgari 2 yıl saklama, zaman senkronizasyonu (NTP), günlüklerin merkezi ve değiştirilemez toplanması | ❌ | Loki/Prometheus 7 gün saklıyor (BİGR'in öngördüğü sürelerin çok altında); merkezi toplama var ama değiştirilemezlik/bütünlük garantisi yok. |
| Sunucu/uygulama güvenliği — sıkılaştırma (hardening) kontrol listeleri, güncel yama yönetimi | ⚠️ | Docker/K8s tabanlı, temel imajlar güncel tutuluyor (Dependabot ile) ama resmi bir sıkılaştırma kontrol listesi/kaydı yok. |
| Ağ güvenliği — DDoS koruması, IDS/IPS, ağ segmentasyonu | ⚠️ | K8s NetworkPolicy segmentasyonu var; DDoS/IDS-IPS çözümü kod tabanından görünmüyor (muhtemelen bulut sağlayıcı/CDN katmanında olabilir, doğrulanamadı). |
| Mobil cihaz yönetimi (MDM) | ❌ | `apps/senseik-mobile` bir Expo uygulaması; kurumsal MDM/cihaz yönetimi politikası yok (muhtemelen bu ürünün kapsamı dışında, son kullanıcı cihazları). |
| Veri sızıntısı önleme (DLP) | ❌ | Belge/kayıt içeriğinin loglanmaması bir DLP unsuru sayılabilir ama ayrılmış bir DLP aracı/politikası yok. |
| USOM'a siber olay bildirimi | ❌ | Olay müdahale süreci olmadığı için bildirim akışı da yok. |
| Yerli/milli ürün tercihi | ❌ | Değerlendirme kapsamı dışında (ticari bulut bileşenleri kullanılıyor — Postgres, Redis, MinIO, hepsi açık kaynak/uluslararası). |
| e-İmza / KEP entegrasyonu | ⚠️ (planlı) | `docs/analysis/kolayik-video/11.3.09-...` dokümanında `ISignatureProvider`/`IKepProvider` portları tasarım aşamasında, henüz kod yok. |
| Bulut güvenliği değerlendirmesi | ❌ | Barındırma sağlayıcısı belirsiz olduğundan değerlendirilemedi. |

---

## 4. Önceliklendirilmiş iş listesi

### P0 — hukuki/itibari risk, en geç bir sonraki sürümde ele alınmalı
1. **Pazarlama sayfası metnini gerçek duruma çek** (`web/public/locales/tr/trust.json`) — yedekleme ve depolama-katmanı şifreleme iddiaları düzeltilsin, ya da bu iki önlem gerçekten uygulanana kadar sayfadan çıkarılsın.
2. **Yedekleme + temel DR planı** — Postgres için düzenli `pg_dump`/PITR (point-in-time recovery), MinIO için versiyonlama/replikasyon, en az bir RPO/RTO hedefi ve geri yükleme testi prosedürü.
3. **TCKN alan-düzeyi şifreleme** — `Employee.NationalId` için IBAN'da kullanılan aynı `IEncryptionProvider` deseni uygulanmalı (mevcut kayıtlar için migrasyon planı gerekir).
4. **KVKK rıza kaydını uçtan uca bağla** — `ConsentRecorder`'ı `IdentityModule`'de kaydet, EF entity/migration ekle; `UserInvitations.cs`'teki "yalnız log" geçici çözümünü kaldır.

### P1 — orta vadeli, mevcut çeyrekte planlanmalı
5. Denetim kaydı (audit log) için DB-düzeyinde değişmezlik (örn. `REVOKE UPDATE, DELETE` uygulama rolünden) ve bir saklama/arşivleme politikası.
6. Bağımlılık taramasına container image tarama (Trivy) ve secret-scanning (gitleaks) ekle; `dotnet list package --vulnerable`/`npm audit` CI adımı.
7. Yazılı olay müdahale süreci + güvenlik iletişim noktası + KVKK 72 saatlik ihlal bildirim prosedürü.
8. Prod sır yönetimini Sealed Secrets/External Secrets Operator'a taşı (k8s README zaten bunu öneriyor, uygulama bekliyor).
9. Log/metrik saklama süresini (7 gün) en azından denetim kaydı için uzat; BİGR'in öngördüğü süreye (kurumsal karar gerektirir) yaklaştır.

### P2 — BGYS kurumsal çalışması (kod dışı, süreç/danışmanlık gerektirir)
10. Bilgi güvenliği politikası, risk kaydı, BGYS kapsam belgesi, iç denetim planı, DÖF süreci — bunlar bir güvenlik/uyum danışmanıyla veya belirlenecek bir BGYS sorumlusuyla yürütülmeli, kod değişikliği değildir.
11. Varlık envanteri ve tedarikçi listesi dokümante edilmeli.
12. Barındırma sağlayıcısı netleşince fiziksel/bulut güvenliği sertifikasyonu (örn. sağlayıcının kendi ISO 27001 belgesi) referans olarak eklenmeli.

---

## Kapsam dışı / bu oturumda doğrulanamayan

- GitHub repo ayarlarındaki branch protection'ın fiilen etkin olup olmadığı (dokümante edilmiş ama bu oturumda `gh` yetkisi yoktu).
- Barındırma/veri merkezi konumu ve sağlayıcının kendi sertifikasyonları.
- Postgres/MinIO'nun altyapı (bulut sağlayıcı) düzeyinde şifrelenip şifrelenmediği — bu k8s/Docker manifestolarının dışında, sağlayıcı konsolunda yapılandırılıyor olabilir.
