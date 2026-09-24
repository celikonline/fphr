# Agent Board — ECC takım orkestrasyonu

Bu klasör, ECC `team-agent-orchestration` skill'inin beklediği **agent Kanban**'ın
depodaki kalıcı hali. Amaç: paralel çalışan ajanların çıktısı sohbette kalmasın,
sahibi ve merge kapısı belli iş kalemlerine dönüşsün.

Mevcut `docs/implementation/paralel-gelistirme-plani.md` (sıcak dosyalar, git
akışı) **üst kural**dır. Board onu değiştirmez, üstüne görünürlük ekler.
PC-A / PC-B ayrımı 2026-09-24'ten beri geçersiz; tüm işler tek makinede.

## Kolonlar

| Kolon | Anlamı | Çıkış kriteri |
|---|---|---|
| `backlog` | Aday kalem, henüz şekillenmedi | Kabul kriterleri yazıldı |
| `ready` | Şekillendi, atanabilir | Sahip + dal/worktree atandı |
| `running` | Ajan üzerinde çalışıyor | Handoff notu ve değişen dosyalar var |
| `review` | Bitti, merge edilmedi | Testler + diff incelemesi + risk kontrolü geçti |
| `blocked` | Dış girdi bekliyor veya kapı kaldı | Blokajın sahibi ve sonraki adımı yazılı |
| `merged` | main'e girdi | PR merge edildi |
| `archived` | Geçersiz | Gerekçe kaydedildi |

Çıkış kriteri yazılmamış kart ilerlemez ("board theater" anti-pattern'i).

## Kart şeması

`cards/*.json`, skill'in şemasını birebir izler:
`id, title, owner, state, branch, worktree, acceptance[], merge_gate, handoff`.

Yeni kart: `cards/TEMPLATE.json` dosyasını kopyala, id'yi sıradaki numara yap.

## Sahiplik (şeritler)

| Şerit | Sahip | Alan | Dal öneki |
|---|---|---|---|
| Tek makine | Tüm modüller, web ve mobil | `a/<konu>` (takım kartları `takim/<konu>`) |

Başka modülün alanına ihtiyaç → önce `*.Contracts`'a küçük, ayrı PR (contract-first).
Sıcak dosyalara (`ModuleCatalog.cs`, `Sense.Migrator/Program.cs`,
`Sense.Worker/Program.cs`, `Sense.slnx`) dokunuş **append-only**.

## Merge kapıları (senseik'e özel komutlar)

Host'ta dotnet yok; .NET komutları SDK konteynerinde çalışır (bkz. `CLAUDE.md`).

| Kapı | Komut |
|---|---|
| Backend derleme | `dotnet build Sense.slnx` (SDK konteyneri) |
| Backend test | `dotnet test` (Testcontainers ile) |
| Web tip kontrolü | `pnpm --filter web typecheck` |
| Web lint | `cd web && pnpm exec eslint src` |
| Web build | `cd web && pnpm exec vite build` |
| Veri yolu | `Sense.Migrator` ile `migrate` + `seed --profile demo` temiz DB'de geçer |
| Durum takibi | İlgili `docs/analysis/kolayik-video/fazN-durum-takibi.md` koda göre güncellendi |

## Günlük akış

1. `dev-team` — PM/Architect/Dev/QA dört bakış, kalem şekillenir (analiz, dosya yazmaz).
2. Karar gerekiyorsa `council`.
3. Kart aç: sahip, dal, kabul kriterleri, merge kapısı.
4. `/orch-add-feature` | `/orch-fix-defect` | `/orch-change-feature` | `/orch-refine-code`
   — Research → Plan (**GATE 1**) → TDD → Review → Commit (**GATE 2**).
5. Çapraz inceleme gerekirse `team-builder`: `csharp-reviewer` + `react-reviewer` + `security-reviewer`.
6. Kart `review` → `merged`; handoff notu `docs/handoffs/` altına.

## Maliyet notu

ECC README'nin uyarısı geçerli: her teammate ayrı context window tüketir. Paralel
takımı çok modüllü iş ve paralel inceleme için kullan; tek dosyalık işte tek
subagent daha ucuz.
