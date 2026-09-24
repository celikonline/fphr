# Superpowers plugin ile birlikte çalışma

`superpowers` (obra/superpowers) skill'leri bu projedeki ECC ve `/takim-kur` akışlarıyla çakışmasın diye:

- **Tek akış:** Bir işte ya superpowers (`brainstorming` → `writing-plans` → `subagent-driven-development`/`executing-plans`) ya ECC (`plan`, `orch-*`, `tdd-guide`) kullanılır; aynı işte ikisi birden başlatılmaz.
- **Kodu takım yazar:** Plan adımlarının kod değişikliği `takim-gelistirici-*` ajanlarına verilir. Superpowers'ın kendi incelemesi `takim-gozden-gecirici`'nin ve test kapısının (`node .claude/takim/betikler/test-kapisi.mjs`) yerini tutmaz; "bitti" demeden ikisi de koşturulur.
- **Branch sonu:** `finishing-a-development-branch`'te yalnız "Push and Create PR" ya da "Keep As-Is" seçilir. "Merge Locally" seçilmez — `main`'e giden tek yol PR.
- **Worktree/branch:** Worktree gerekiyorsa native araç (`EnterWorktree`) kullanılır. Branch adı `paralel-gelistirme-plani.md`'ye uyar: `a/<topic>` ya da `b/<module>-<topic>`.
- **Belgeler:** Spec `docs/superpowers/specs/`, plan `docs/superpowers/plans/` altında kalır; iş kartı (`docs/takim/isler/<id>.json`) varsa bu dosyalara bağlantı verir. Durum takibi (`faz*-durum-takibi.md`) yine koda göre güncellenir.
