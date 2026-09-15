# Faz 5 Durum Takibi (Performance — performans değerlendirme çekirdeği)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 5 satırları (#75–80), ayrıntı [11.3.10-performans.md](./11.3.10-performans.md) "SenseİK tasarım kararı" bölümü. Kapsam kullanıcı onayıyla daraltıldı: **yalnız Performance çekirdeği**, Compensation/Payroll'a bağımlı hiçbir şey yok (`docs/implementation/paralel-gelistirme-plani.md` §7 bu modülü Payroll/Attendance'a bağımlı göstermişti — kullanıcı bunu görüp kapsamı sadece çekirdekle sınırladı).

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

## Tasarım dokümanından kasıtlı sapmalar

1. **Rapor üretimi henüz yok (P6 kapsamında kalacak, bu oturumda yapılmadı).** QuestPDF bağımlılığı repoda tanımlı ama hiç kullanılmamıştı (bu modül dahil hâlâ kullanılmıyor); Hangfire `IBackgroundJobService`/`JobType` iskeleti var ama handler'ları stub; Documents'a sunucu tarafından üretilmiş dosya yazma arayüzü yok (`IFileStorage` yalnız presigned-URL/tarayıcı-upload modeli). Planlanan çözüm: rapor endpoint'i canlı/senkron QuestPDF render edecek, ayrı `ReviewReport`/`FileId` tablosu olmayacak.
2. **Hedef akışı (`ReviewKind.CompetencyAndGoal`) uygulanmadı.** `ReviewCycle.Create` bu türü şimdilik `performance.cycle.goal_kind_not_yet_supported` hatasıyla reddediyor; yalnız `Competency` türü çalışır. `Goal`/`GoalCategory` entity'leri, `AddGoal/ApproveGoals/EnterGoalResult/ApproveGoalResults` komutları yok.
3. **Hedef onayının (P5'te) Workflow motorundan değil, doğrudan yöneticinin komutundan geçmesi planlanıyor** — tasarım dokümanındaki ekran akışı (Hedefler sayfasında "Onayla" butonu) tek adımlı doğrudan onay, çok adımlı bir inbox akışı değil.
4. **`ReviewTemplate` ayrı ekran/kütüphane olarak yok** — sihirbaz "Kaydet ve bitir" dediğinde config + `ReviewCycle` tek `CreateReviewCycleCommand`'de birlikte oluşturuluyor (ad hoc). "Şablondan başlat" [Aday] kapsam dışı.
5. **`Employee.Contracts` küçük bir ek aldı (P0):** `IEmployeeProfileLookup.GetProfileDetailsAsync` (HireDate, WorkPhone) — mevcut `EmployeeContact`'ın taşımadığı, ileride rapor için gereken alanlar. Additive, mevcut hiçbir tüketiciyi bozmuyor.
6. **`TenantSettings.Performance` kiracı ayarları yok** — `EqualWeightWhenBlank` MVP'de sabit `true` olarak kodlandı, `DefaultMinEvaluators`/`AutoAnonymizePeerBelow` vb. henüz yapılandırılamıyor.

## Paketler

| Paket | Kapsam | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| P0 | `Employee.Contracts.IEmployeeProfileLookup` eki (contract-first) | ✅ Bitti | `EmployeeDirectoryContracts.cs`, `EmployeeDirectory.cs` (`EmployeeProfileLookup`), `EmployeeModule.AddModule` kaydı | Yalnız HireDate/WorkPhone; pozisyon/fotoğraf/org birim yolu Employee domain'inde henüz alan olarak yok, eklenmedi (uydurma alan istenmedi) |
| P1 | Modül iskeleti: 5 proje, şema `performance`, `PerformanceDbContext`, design-time factory, `InitialPerformance` migration, `Sense.slnx`/`ModuleCatalog`/`Migrator`/`Worker` kaydı, test projesi iskeleti | ✅ Bitti | `src/Modules/Performance/*`, migration `20260915212816_InitialPerformance`, `dotnet build Sense.slnx` 0 hata, mimari testler (34/34) Performance'ı otomatik kapsıyor | — |
| P2 | Config domain: `ReviewCycle{Kind,RatingScale,DefaultCommentMode,EvaluatorLabelMode,ReportShowPhoto,ResultVisibility}`, `ReviewCompetency`/`ReviewQuestion`, `ReviewRelationConfig`; `CreateReviewCycleCommand`/`UpdateReviewCycleDraftCommand` | ✅ Bitti | `ReviewCycle.cs` (`Create`/`UpdateDraft`), `ReviewCycleCommands.cs`, validasyon: kategori seti tam 4 (Self/Manager/Subordinate/Peer), en az 1 yetkinlik + 1 soru, `CompetencyAndGoal` reddi | `RatingScale`/`GoalEntryBy` DB'de seed tablosu değil, sabit enum (plan sadeleştirmesi) |
| P3 | Katılımcılar + yayınlama + form doldurma: `ReviewParticipant`/`ReviewAssignment`/`ReviewResponse`, `AddParticipantsCommand`, `PublishReviewCycleCommand`, `SubmitReviewResponseCommand` | ✅ Bitti | `ReviewParticipant.cs`, `ReviewCycleCommands.cs` (Publish), `ReviewAssignmentCommands.cs`; iş kuralları: "Kendisi" kategorisine katılımcı dışı kişi eklenemez, pasif kategoriye değerlendirici eklenemez, üst sınır = katılımcılar arası en az atanan sayı | — |
| P4 | Puanlama + tamamlama: `ScoreCalculator` (soru→yetkinlik→genel ağırlıklı ortalama, boşsa eşit dağıtım), doğal tamamlama (`SubmitResponse` içinde), `ForceCompleteReviewCycleCommand` | ✅ Bitti | `ScoreCalculator.cs`, `ReviewCycle.ForceComplete`; birim testleri (`ReviewCycleTests.cs`, `ForceCompleteTests.cs`, 8/8 yeşil) tasarım dokümanının somut örneğini birebir doğruluyor: 10 çalışan/7'sine 5 üst-3'üne 2 üst → üst sınır 2 (`Publish_MinRequiredAtOrBelowSmallestAssignedCount_Succeeds`/`..._IsRejected`); ForceComplete eligibility + Skipped + `PERF_FORCE_COMPLETE_NO_ELIGIBLE` | "7 değerlendiriciden 4'ü tamamlayınca puan 92,5" örneği dokümanda alt veriler (soru bazlı puanlar) verilmediği için birebir üretilemedi — onun yerine ağırlıklı/eşit dağıtım formülünü doğrulayan kontrollü bir senaryo yazıldı (`Score_ExplicitRelationWeights_UsesWeightedAverageNotSimpleAverage`) |
| P5 | Hedef akışı (`Goal`/`GoalCategory`, `AddGoal/ApproveGoals/EnterGoalResult/ApproveGoalResults`, `GoalEntryBy` dallanması) | ❌ Başlanmadı | — | `ReviewKind.CompetencyAndGoal` bu yüzden hâlâ reddediliyor |
| P6 | Rapor: QuestPDF `IndividualReviewReportDocument` (Detaylı/Özet), anonimleştirme, indirme endpoint'i | ❌ Başlanmadı | — | P0'daki `IEmployeeProfileLookup` hazır; pozisyon/fotoğraf alanları için Employee'ye ayrı bir ek gerekecek |
| P7 | İzinler + seed + event'ler: `PerformancePermissions` (8 anahtar), `SharedResource{,.en}.resx` girdileri, `SystemRoleDefinitions` (`performance.form.fill` Employee/Manager'a eklendi; `performance.*` zaten HR/Owner/Admin'de vardı), Contracts event'leri (`ReviewCyclePublished`, `ReviewParticipantCompleted`) | ✅ Bitti (çekirdek izinler) | `PerformancePermissions.cs`, resx girdileri (tr/en), `SystemRoleDefinitions.cs` diff, `Performance.Contracts/Events/PerformanceIntegrationEvents.cs`, `outbox.Enqueue` çağrıları (Publish/ForceComplete/SubmitResponse) | Goal/report izinleri (P5/P6 tamamlanınca eklenecek); event'lerin hiçbir tüketicisi yok (Notifications entegrasyonu kapsam dışı bırakıldı) |
| P8 | Web: sihirbaz, admin detay, self-servis, navigasyon, i18n | ❌ Başlanmadı | — | `web/src/config/navigation.ts`'de `performance` launcher girdisi zaten `phase: 5` ile duruyordu — güncellenmedi |

## Doğrulama

- `dotnet build Sense.slnx`: 0 hata.
- `dotnet test tests/Modules/Sense.Modules.Performance.Tests`: 8/8 yeşil.
- `dotnet test tests/Sense.Tests.Architecture`: 34/34 yeşil (Performance otomatik kapsanıyor — `Modules_TalkOnlyThroughContracts`, `Modules_HaveOneDbContextPerModule`, `PermissionKeys_FollowNamingConvention` dahil).
- `dotnet test tests/Modules/Sense.Modules.Identity.Tests`: 46/46 yeşil (SystemRoleDefinitions değişikliği sonrası).
- `dotnet test tests/Modules/Sense.Modules.Employee.Tests`: 84/84 yeşil (P0 contract eki sonrası).
- Web (P8 henüz yok) ve tam `dotnet test Sense.slnx` bu oturumda çalıştırılmadı.

## Paket sırası

P0 (Employee.Contracts eki) → P1 (modül iskeleti) → P2 (config/oluşturma) → P3 (katılımcılar/yayınlama/form) → P4 (puanlama/tamamlama) — **bu oturumda buraya kadar bitti** → P5 (hedef akışı) → P6 (rapor) → P7 (izin/seed — çekirdek kısmı zaten P2-P4 ile birlikte yapıldı, kalan goal/report anahtarları P5/P6'yı bekliyor) → P8 (web).
