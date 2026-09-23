# Takım çalışma kuralı (/takim-kur)

Bu projede `/takim-kur` ile kurulmuş bir ajan takımı var; ayrıntı: `.claude/takim/TAKIM.md`.
- Kod değişikliğini `takim-gelistirici*` ajanlarına ver; birden çok yığına dokunan işte önce `takim-orkestrator` planı.
- Bir işi "bitti" saymadan önce `takim-gozden-gecirici`'yi çalıştır; o, test kapısını kendisi koşturur.
- Test kapısı: `node .claude/takim/betikler/test-kapisi.mjs` (argümansız). Testleri silmek/atlamak/filtrelemek yasak.
- Korumalı dallara (main) doğrudan yazılmaz; iş çalışma dalında, birleştirme PR ile.
- Sır dosyaları okunmaz/yazılmaz. `.claude/takim/**` ve `.claude/settings*.json` elle değiştirilmez.
- İş durumu: dosya — `docs/takim/isler/<id>.json` (şablon `.claude/takim/is-karti.json`), handoff `docs/takim/handoff/<id>.md`.
