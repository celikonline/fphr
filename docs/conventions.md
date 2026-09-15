# Kodlama ve Depo Kuralları

- **Dil:** kod ve tanımlayıcılar İngilizce, kullanıcıya görünen metinler Türkçe (`tr` varsayılan, `en` ikincil). Domain hata mesajları `Errors.tr.resx`.
- **Adlandırma:** `Sense.*` namespace; modül projeleri `Sense.Modules.<Modül>.{Domain,Application,Infrastructure,Contracts,Api}`; PostgreSQL snake_case; API kebab-case çoğul kaynaklar.
- **Commit:** Conventional Commits (`feat(leave): …`, `fix(payroll): …`); squash merge; `main` korumalı.
- **PR:** her PR build + test + architecture + OpenAPI snapshot geçmeli.
- **Katmanlar:** ADR 0002 (Onion). Domain'de NuGet yok; Application Infrastructure'ı görmez.
- **Liste uçları:** `PagedQuery` → `PagedResult<T>`; sıralama alanları whitelist; `?export=xlsx|csv`.
- **Hata:** RFC 9457 ProblemDetails, `code` alanı (`leave.insufficient_balance`), 404 = yok/kapsam dışı.
- **Zaman:** DB UTC (`timestamptz`), gün alanları `date`; tenant TZ `Europe/Istanbul`.
- **Para:** `Money` (decimal(18,4), TRY); yuvarlama HALF_UP 2 hane yalnız çıktı katmanında.
- **Gizli bilgi:** asla commit edilmez; lokal `dotnet user-secrets`, `.env` gitignore'da.
- **Handler adlandırma:** `{Action}Handler` (ör. `CreateUserHandler`, `ListRolesHandler`) — **`{Action}CommandHandler`/`{Action}QueryHandler` DEĞİL**. `sealed` olmalı. `Sense.Tests.Architecture.OnionArchitectureTests.Handlers_FollowNamingConventions` ile zorlanır.
- **İzin anahtarı:** `{module}.{resource_plural}.{action}` snake_case (ör. `identity.users.create`, `documents.files.read`); `[RequiresPermission("…")]` ile command/query üzerinde bildirilir, modülün `*Permissions` sınıfında (Contracts projesi) sabit olarak tanımlanır. `PermissionKeys_FollowNamingConvention` ile zorlanır.
- **Kiracı izolasyonu:** her modülün `ModuleDbContext`'i `TenantId` üzerinden otomatik global query filter uygular (`ITenantEntity`); doğrudan `DbSet` erişimi/filtre bypass'ı yalnızca testte (`AsTenantNonFiltered()`) yapılır. Yeni entity'ler `ITenantEntity` implement etmeli. Ayrıntı: `tests/README.md` (Tenant Isolation Validation).
- **Test:** yeni modül/handler için `tests/README.md`'deki `TestFixture` tabanlı entegrasyon testi deseni izlenir (`CreateTenantAsync` → `SetCurrentUser` → `ExecuteCommandAsync`/`ExecuteQueryAsync` → `ShouldBeSuccess`/`ShouldContainAuditEvent`); mimari kurallar her PR'da `Sense.Tests.Architecture` ile otomatik doğrulanır.
- **Yeni modül iskeleti:** `./build/new-module.ps1 -Name <PascalCase>` — 5 projeyi (Domain/Application/Infrastructure/Api/Contracts) mevcut modül desenine göre üretir ve `Sense.slnx`'e ekler; sonraki adımlar (ModuleCatalog kaydı, migration) betiğin çıktısında listelenir.
