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
